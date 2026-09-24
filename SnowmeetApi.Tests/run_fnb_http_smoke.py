"""End-to-end HTTP smoke test for the food-inventory API against a disposable SQL Server database.

Builds the API, creates an isolated database with the same bootstrap as
run_fnb_sqlserver_integration.py, seeds staff with real mini-program sessions, starts the
API locally on that database and drives every Fnb* endpoint over HTTP: auth and cost
hiding, idempotent replays, FEFO, shortage, stocktake conflicts and parallel posting.
Only schema is read from snowmeet_new; the disposable database is always dropped.

Usage: ODBCSYSINI=... python3 SnowmeetApi.Tests/run_fnb_http_smoke.py
"""

from __future__ import annotations

import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen
from uuid import uuid4

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_fnb_sqlserver_integration as base  # noqa: E402  (sets TEST_DATABASE on import)

API_DLL = base.API_DIR / "bin" / "Debug" / "net9.0" / "SnowmeetApi.dll"
SESSION_TABLES = ("mini_session", "member", "social_account_for_job", "staff_social_account", "member_social_account")
TODAY = (datetime.now(timezone.utc) + timedelta(hours=8)).date()
RESULTS: list[tuple[bool, str, str]] = []
BASE_URL = ""


def check(name: str, ok: bool, detail: object = "") -> bool:
    RESULTS.append((bool(ok), name, "" if ok else str(detail)[:400]))
    print(("  PASS " if ok else "  FAIL ") + name + ("" if ok else f"  -> {str(detail)[:400]}"), flush=True)
    return bool(ok)


def call(method: str, path: str, session: str | None, params: dict | None = None, body: object = None):
    query = dict(params or {})
    if session is not None:
        query["sessionKey"] = session
    data = None if body is None else json.dumps(body).encode()
    request = Request(f"{BASE_URL}/api/{path}?{urlencode(query)}", data=data, method=method,
                      headers={"Content-Type": "application/json"} if data else {})
    try:
        with urlopen(request, timeout=120) as response:
            status, text = response.status, response.read()
    except HTTPError as error:
        status, text = error.code, error.read()
    try:
        payload = json.loads(text)
    except ValueError:
        payload = {"raw": text[:300].decode(errors="replace")}
    return status, payload


def get(path, session, **params):
    return call("GET", path, session, params)


def post(path, session, body):
    return call("POST", path, session, None, body)


def ok(result) -> bool:
    return result[0] == 200 and result[1].get("code") == 0


def data(result):
    return result[1].get("data")


def copy_session_tables() -> None:
    with base.connect(base.TEST_DATABASE) as target, base.connect(base.SOURCE_DATABASE) as source:
        cursor = target.cursor()
        for table in SESSION_TABLES:
            cursor.execute(f"SELECT TOP (0) * INTO [dbo].[{table}] FROM [{base.SOURCE_DATABASE}].[dbo].[{table}]")
            key = "session_key, session_type" if table == "mini_session" else "id"
            cursor.execute(f"ALTER TABLE [dbo].[{table}] ADD CONSTRAINT [PK_test_{table}] PRIMARY KEY ({key})")
        placeholders = ",".join("?" * len(SESSION_TABLES))
        defaults = source.cursor().execute(
            f"""SELECT t.name, c.name, d.definition FROM sys.default_constraints AS d
            JOIN sys.tables AS t ON t.object_id = d.parent_object_id
            JOIN sys.columns AS c ON c.object_id = t.object_id AND c.column_id = d.parent_column_id
            WHERE t.name IN ({placeholders})""", SESSION_TABLES).fetchall()
        for table, column, definition in defaults:
            cursor.execute(f"ALTER TABLE [dbo].[{table}] ADD CONSTRAINT [DF_test_{table}_{column}] DEFAULT {definition} FOR [{column}]")


def seed() -> dict:
    """Two shops, a manager and a cook in the kitchen shop, a manager in another shop, photos."""
    with base.connect(base.TEST_DATABASE) as target:
        cursor = target.cursor()

        def scalar(sql, *args):
            return cursor.execute(sql, *args).fetchone()[0]

        shops = [scalar("INSERT INTO shop_list (name, code, sort, lat_from, lat_to, long_from, long_to, sale, care, rent, restuarant) "
                        "OUTPUT INSERTED.id VALUES (?, ?, 900, 0, 0, 0, 0, 0, 0, 0, 1)", name, code)
                 for name, code in (("多呆一会儿吧", "DDY"), ("别的店", "BDD"))]
        sessions = {}
        for role, level, shop in (("manager", 200, shops[0]), ("cook", 100, shops[0]), ("outsider", 300, shops[1])):
            staff_id = scalar("INSERT INTO staff (name, gender, title_level, valid, base_shop_id) OUTPUT INSERTED.id VALUES (?, '男', ?, 1, ?)",
                              "冒烟" + role, level, shop)
            member_id = scalar("INSERT INTO member (is_merge) OUTPUT INSERTED.id VALUES (0)")
            job_id = scalar("INSERT INTO social_account_for_job (cell, wechat_mini_openid, member_id) OUTPUT INSERTED.id VALUES ('', ?, ?)",
                            "openid-" + role, member_id)
            cursor.execute("INSERT INTO staff_social_account (staff_id, social_account_id, start_date, valid) VALUES (?, ?, ?, 1)",
                           staff_id, job_id, datetime.now() - timedelta(days=1))
            key = "smoke" + uuid4().hex
            cursor.execute("INSERT INTO mini_session (session_key, session_type, valid, expire_date, member_id) VALUES (?, 'wechat_mini_openid', 1, ?, ?)",
                           key, datetime.now() + timedelta(days=1), member_id)
            sessions[role] = key
        photos = [scalar("INSERT INTO mini_upload (file_path_name, purpose) OUTPUT INSERTED.id VALUES (?, '食材批次')", f"/smoke/{i}.jpg")
                  for i in range(12)]
    return {"shop": shops[0], "other_shop": shops[1], "photos": photos, **sessions}


def free_port() -> int:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def start_api(workdir: Path) -> subprocess.Popen:
    connection = ";".join(f"{key}={base.TEST_DATABASE if key == 'database' else value}" for key, value in base.parts.items())
    (workdir / "config.sqlServer").write_text(connection)
    for name in ("appsettings.json", "appsettings.Development.json"):
        if (base.API_DIR / name).exists():
            shutil.copy(base.API_DIR / name, workdir / name)
    port = free_port()
    global BASE_URL
    BASE_URL = f"http://127.0.0.1:{port}"
    log = open(workdir / "api.log", "w")
    process = subprocess.Popen(["dotnet", str(API_DLL), "--urls", BASE_URL], cwd=workdir, stdout=log, stderr=subprocess.STDOUT)
    for _ in range(90):
        time.sleep(1)
        try:
            status, _ = get("FnbCatalog/GetUnits", "probe", shopId=0)
            if status == 200:
                return process
        except (URLError, ConnectionError, OSError):
            pass
        if process.poll() is not None:
            break
    raise RuntimeError("本地 API 未能启动，见 " + str(workdir / "api.log"))


def iso(day) -> str:
    return day.isoformat()


def receipt(ctx, session, item_id, quantity, *, unit="g", price=0, form="bulk", pack=None, pack_name=None,
            expire=None, request_id=None, storage="chilled", photo=0):
    body = {"shopId": ctx["shop"], "requestId": request_id or str(uuid4()), "itemId": item_id,
            "batchNo": "SM" + uuid4().hex[:8], "stockForm": form, "storageType": storage, "storageLocation": None,
            "quantity": quantity, "inputUnitCode": unit, "unitPrice": price, "productionDate": None,
            "shelfLifeValue": None, "shelfLifeUnit": None, "expireDate": iso(expire or TODAY + timedelta(days=30)),
            "warnDays": 2, "imageIds": [ctx["photos"][photo]], "packSize": pack, "packUnitName": pack_name,
            "openStorageType": "chilled" if form == "sealed" else None, "openShelfLifeDays": 3 if form == "sealed" else None,
            "expirySource": "manual", "expiryNote": None}
    return post("FnbInventory/PostReceipt", session, body)


def stock_of(ctx, item_id) -> dict:
    rows = data(get("FnbInventory/GetStock", ctx["manager"], shopId=ctx["shop"], itemId=item_id)) or []
    return rows[0] if rows else {"totalQty": 0, "availableQty": 0, "sealedQty": 0}


def scenarios(ctx: dict) -> None:
    shop, mgr, cook, out = ctx["shop"], ctx["manager"], ctx["cook"], ctx["outsider"]

    print("\n[鉴权与成本隐藏]")
    check("无会话返回 2", get("FnbCatalog/GetUnits", "nope", shopId=shop)[1].get("code") == 2)
    check("跨店员工返回 3", get("FnbCatalog/GetUnits", out, shopId=shop)[1].get("code") == 3)
    units = get("FnbCatalog/GetUnits", cook, shopId=shop)
    check("员工可读单位 5 条", ok(units) and len(data(units)) == 5, units)
    denied = post("FnbCatalog/SaveCategory", cook, {"shopId": shop, "id": 0, "level": 1, "name": "员工建", "sort": 1, "valid": True})
    check("员工维护分类返回 3", denied[1].get("code") == 3, denied)

    print("\n[分类、规则、食材]")
    l1 = post("FnbCatalog/SaveCategory", mgr, {"shopId": shop, "id": 0, "parentId": None, "level": 1, "name": "生鲜", "sort": 1, "valid": True})
    check("店长建一级分类", ok(l1), l1)
    l2 = post("FnbCatalog/SaveCategory", mgr, {"shopId": shop, "id": 0, "parentId": data(l1)["id"], "level": 2, "name": "蔬菜类",
                                                  "defaultStorage": "chilled", "sort": 1, "valid": True})
    check("店长建二级分类（只填名称和储存方式）", ok(l2), l2)
    no_storage = post("FnbCatalog/SaveCategory", mgr, {"shopId": shop, "id": 0, "parentId": data(l1)["id"], "level": 2, "name": "无储存",
                                                        "sort": 2, "valid": True})
    check("二级分类不选储存方式被拒 code 1", no_storage[1].get("code") == 1, no_storage)
    sub = data(l2)["id"]
    items = {}
    for code, name, kind, base_unit, input_unit, open_days in (("VEG001", "大白菜", "raw", "g", "kg", None), ("SAU001", "番茄酱", "raw", "ml", "ml", 7),
                                                               ("DGH001", "面团", "prepared", "piece", "piece", None), ("POT001", "土豆", "raw", "g", "g", None)):
        saved = post("FnbCatalog/SaveMaterial", mgr, {"shopId": shop, "id": 0, "code": code, "name": name, "categoryId": sub,
                                                      "itemType": kind, "baseUnitCode": base_unit, "defaultInputUnitCode": input_unit,
                                                      "warnDays": 1, "defaultOpenStorage": "chilled" if open_days else None,
                                                      "defaultOpenDays": open_days, "imageId": None, "remark": None, "valid": True})
        check(f"店长建食材 {name}", ok(saved), saved)
        items[name] = data(saved)["id"]
    sauce = get("FnbCatalog/GetMaterial", cook, shopId=shop, id=items["番茄酱"])
    check("食材保存开封后默认", ok(sauce) and data(sauce)["default_open_storage"] == "chilled" and data(sauce)["default_open_days"] == 7, sauce)
    no_warn = post("FnbCatalog/SaveMaterial", mgr, {"shopId": shop, "id": 0, "code": "BAD001", "name": "缺提醒", "categoryId": sub,
                                                     "itemType": "raw", "baseUnitCode": "g", "defaultInputUnitCode": "g", "valid": True})
    check("食材缺临期提醒天数被拒 code 1", no_warn[1].get("code") == 1, no_warn)
    rules = [post("FnbCatalog/SaveShelfLifeRule", mgr, {"shopId": shop, "id": 0, "itemId": items["大白菜"], "storageType": "chilled",
                                                         "productionMonth": m, "shelfLifeValue": 7 if m in (6, 7, 8, 9) else 10,
                                                         "shelfLifeUnit": "day", "remark": None, "valid": True}) for m in range(1, 13)]
    check("大白菜 12 个月的冷藏规则全部写入", all(ok(r) for r in rules), [r for r in rules if not ok(r)][:1])
    dup = post("FnbCatalog/SaveShelfLifeRule", mgr, {"shopId": shop, "id": 0, "itemId": items["大白菜"], "storageType": "chilled",
                                                      "productionMonth": 1, "shelfLifeValue": 3, "shelfLifeUnit": "day", "valid": True})
    check("同食材同月重复规则被拒 code 1", dup[1].get("code") == 1, dup)
    own_rules = get("FnbCatalog/ListShelfLifeRules", cook, shopId=shop, itemId=items["大白菜"])
    check("按食材查规则 12 条", ok(own_rules) and len(data(own_rules)) == 12, own_rules)
    listed = get("FnbCatalog/ListMaterials", cook, shopId=shop, categoryId=sub)
    check("员工按分类查食材", ok(listed) and data(listed)["total"] == 4, listed)
    preview = get("FnbInventory/PreviewExpiry", cook, shopId=shop, itemId=items["大白菜"], storageType="chilled", productionDate="2026-07-10")
    check("PreviewExpiry 按 7 月（高温档）规则 = 生产日 + 7 天", ok(preview) and data(preview)["expireDate"] == "2026-07-17", preview)
    none_rule = get("FnbInventory/PreviewExpiry", cook, shopId=shop, itemId=items["大白菜"], storageType="frozen", productionDate="2026-07-10")
    check("无规则储存方式返回 rule=null", ok(none_rule) and data(none_rule)["rule"] is None, none_rule)
    sibling = get("FnbInventory/PreviewExpiry", cook, shopId=shop, itemId=items["土豆"], storageType="chilled", productionDate="2026-07-10")
    check("同分类其他食材不继承大白菜的规则", ok(sibling) and data(sibling)["rule"] is None, sibling)

    print("\n[入库与幂等]")
    request_id = str(uuid4())
    first = receipt(ctx, cook, items["大白菜"], 10, unit="kg", price=4, request_id=request_id)
    check("员工散装入库 10kg → 10000g", ok(first) and data(first)["quantity"] == 10000, first)
    check("员工入库结果不含金额", ok(first) and data(first)["amount"] is None, first)
    replay = receipt(ctx, cook, items["大白菜"], 10, unit="kg", price=4, request_id=request_id)
    check("同 requestId 重放 replayed=true 且单号相同", ok(replay) and data(replay)["replayed"] and data(replay)["documentId"] == data(first)["documentId"], replay)
    check("重放后库存仍为 10000g", stock_of(ctx, items["大白菜"])["totalQty"] == 10000, stock_of(ctx, items["大白菜"]))
    sealed = receipt(ctx, mgr, items["番茄酱"], 3, unit="ml", price=12, form="sealed", pack=1000, pack_name="瓶")
    check("店长封装入库 3 瓶×1000ml，返回金额 36", ok(sealed) and data(sealed)["quantity"] == 3000 and data(sealed)["amount"] == 36, sealed)
    expired = receipt(ctx, cook, items["大白菜"], 1, unit="kg", expire=TODAY - timedelta(days=1))
    check("[需求] 已过期批次不允许入库", expired[1].get("code") == 1, expired)
    batches_cook = get("FnbInventory/ListBatches", cook, shopId=shop)
    batches_mgr = get("FnbInventory/ListBatches", mgr, shopId=shop)
    check("员工批次列表 stock_amount 为 null", ok(batches_cook) and all(r["stock"]["stock_amount"] is None for r in data(batches_cook)["rows"]), batches_cook)
    check("店长批次列表有 stock_amount", ok(batches_mgr) and any(r["stock"]["stock_amount"] for r in data(batches_mgr)["rows"]), batches_mgr)

    print("\n[开封、报损、临期]")
    sealed_batch = data(sealed)["batchId"]
    opened = post("FnbInventory/PostOpen", cook, {"shopId": shop, "requestId": str(uuid4()), "parentBatchId": sealed_batch, "packCount": 1})
    check("员工开封 1 瓶放出 1000ml", ok(opened) and data(opened)["quantity"] == 1000, opened)
    tomato = stock_of(ctx, items["番茄酱"])
    check("开封后可用 1000ml、未开封 2000ml", tomato["availableQty"] == 1000 and tomato["sealedQty"] == 2000, tomato)
    waste_denied = post("FnbInventory/PostWaste", cook, {"shopId": shop, "requestId": str(uuid4()), "batchId": data(opened)["batchId"],
                                                        "quantity": 100, "reasonCode": "damage", "remark": None})
    check("员工报损返回 3", waste_denied[1].get("code") == 3, waste_denied)
    waste_id = str(uuid4())
    waste_body = {"shopId": shop, "requestId": waste_id, "batchId": data(opened)["batchId"], "quantity": 100, "reasonCode": "damage", "remark": "洒了"}
    wasted = post("FnbInventory/PostWaste", mgr, waste_body)
    check("店长报损 100ml", ok(wasted), wasted)
    check("报损重放 replayed=true", data(post("FnbInventory/PostWaste", mgr, waste_body))["replayed"])
    summary = get("FnbReport/GetExpirySummary", cook, shopId=shop)
    check("临期汇总返回在库批次与状态", ok(summary) and data(summary)["total"] >= 3 and all("status" in r for r in data(summary)["rows"]), summary)
    label = get("FnbInventory/GetLabelData", cook, shopId=shop, batchId=sealed_batch)
    check("标签数据含批号与扫码地址", ok(label) and data(label)["row"]["batch_no"] and "id=" in data(label)["scanUrl"], label)

    print("\n[菜品与配方]")
    check("员工建菜品返回 3", post("FnbRecipe/SaveDish", cook, {"shopId": shop, "id": 0, "name": "x", "salePrice": 1, "categoryName": "热菜", "valid": True})[1].get("code") == 3)
    dish = post("FnbRecipe/SaveDish", mgr, {"shopId": shop, "id": 0, "name": "酸菜白肉锅", "salePrice": 68, "categoryId": None, "categoryName": "热菜", "valid": True})
    check("店长建菜品并自动建标准份", ok(dish) and data(dish)["specId"], dish)
    bare = post("FnbRecipe/SaveDish", mgr, {"shopId": shop, "id": 0, "name": "无配方菜", "salePrice": 10, "categoryName": "热菜", "valid": True})
    dishes = get("FnbRecipe/ListDishes", cook, shopId=shop)
    check("员工 ListDishes 看到 2 道菜及分类", ok(dishes) and len(data(dishes)["dishes"]) == 2 and data(dishes)["categories"], dishes)
    draft = post("FnbRecipe/SaveRecipeDraft", mgr, {"shopId": shop, "id": 0, "recipeType": "dish", "dishSpecId": data(dish)["specId"],
                                                    "outputItemId": None, "outputQty": 1, "remark": None, "rowVersion": None,
                                                    "lines": [{"itemId": items["大白菜"], "quantity": 400, "sort": 1, "remark": None},
                                                              {"itemId": items["番茄酱"], "quantity": 20, "sort": 2, "remark": None}]})
    check("店长存菜品配方草稿", ok(draft), draft)
    stale = post("FnbRecipe/PublishRecipe", mgr, {"shopId": shop, "recipeId": data(draft)["id"], "rowVersion": "AAAAAAAAAAA="})
    check("旧 rowVersion 发布返回 4", stale[1].get("code") == 4, stale)
    published = post("FnbRecipe/PublishRecipe", mgr, {"shopId": shop, "recipeId": data(draft)["id"], "rowVersion": data(draft)["rowVersion"]})
    check("发布配方（字符串 recipeId）", ok(published), published)
    row = next(d for d in data(get("FnbRecipe/ListDishes", cook, shopId=shop))["dishes"] if d["productId"] == data(dish)["productId"])
    check("ListDishes 显示已发布版本", row["publishedRecipeId"] == data(draft)["id"] and row["publishedVersion"] == 1, row)
    got = get("FnbRecipe/GetRecipe", cook, shopId=shop, recipeId=data(draft)["id"])
    check("员工 GetRecipe 取到 2 行用料", ok(got) and len(data(got)["lines"]) == 2, got)
    prep_draft = post("FnbRecipe/SaveRecipeDraft", mgr, {"shopId": shop, "id": 0, "recipeType": "prep", "dishSpecId": None,
                                                         "outputItemId": items["面团"], "outputQty": 10, "rowVersion": None,
                                                         "lines": [{"itemId": items["大白菜"], "quantity": 1000, "sort": 1}]})
    post("FnbRecipe/PublishRecipe", mgr, {"shopId": shop, "recipeId": data(prep_draft)["id"], "rowVersion": data(prep_draft)["rowVersion"]})
    before = stock_of(ctx, items["大白菜"])["availableQty"]
    made = post("FnbInventory/PostPreparation", cook, {"shopId": shop, "requestId": str(uuid4()), "recipeId": data(prep_draft)["id"],
                                                        "outputQuantity": 10, "batchNo": "DGH" + uuid4().hex[:6], "storageType": "chilled",
                                                        "expireDate": iso(TODAY + timedelta(days=2)), "warnDays": 1,
                                                        "imageIds": [ctx["photos"][1]], "expiryNote": None})
    check("员工制作 10 个面团", ok(made) and data(made)["quantity"] == 10, made)
    check("制作核销白菜 1000g", before - stock_of(ctx, items["大白菜"])["availableQty"] == 1000, stock_of(ctx, items["大白菜"]))
    before = stock_of(ctx, items["大白菜"])["availableQty"]
    too_much = post("FnbInventory/PostPreparation", cook, {"shopId": shop, "requestId": str(uuid4()), "recipeId": data(prep_draft)["id"],
                                                            "outputQuantity": 1000, "batchNo": "DGH" + uuid4().hex[:6], "storageType": "chilled",
                                                            "expireDate": iso(TODAY + timedelta(days=2)), "warnDays": 1,
                                                            "imageIds": [ctx["photos"][1]], "expiryNote": None})
    check("原料不足时制作被拒且库存不变", too_much[1].get("code") in (1, 4) and stock_of(ctx, items["大白菜"])["availableQty"] == before, too_much)

    print("\n[厨房单与出餐]")
    order_body = {"shopId": shop, "requestId": str(uuid4()), "displayNo": None, "tableNo": "A3", "remark": "不要葱",
                  "lines": [{"productId": data(dish)["productId"], "quantity": 2, "remark": None}]}
    order = post("FnbKitchen/CreateManualOrder", cook, order_body)
    check("员工手动建单，配方已发布 → verified", ok(order) and data(order)["reviewStatus"] == "verified", order)
    check("建单重放返回同一 orderId", data(post("FnbKitchen/CreateManualOrder", cook, order_body))["orderId"] == data(order)["orderId"])
    listed_orders = get("FnbKitchen/ListOrders", cook, shopId=shop, businessDate=iso(TODAY))
    check("ListOrders 按今日营业日查到订单", ok(listed_orders) and data(listed_orders)["total"] >= 1, listed_orders)
    preview_serve = get("FnbKitchen/PreviewServe", cook, shopId=shop, orderId=data(order)["orderId"])
    needs = {n["itemId"]: n for n in data(preview_serve) or []}
    check("出餐预览：白菜 800g、番茄酱 40ml", ok(preview_serve) and needs[items["大白菜"]]["plannedQuantity"] == 800 and needs[items["番茄酱"]]["plannedQuantity"] == 40, preview_serve)
    served = post("FnbKitchen/PostServe", cook, {"shopId": shop, "requestId": str(uuid4()), "orderId": data(order)["orderId"]})
    check("员工出餐过账", ok(served), served)
    again = post("FnbKitchen/PostServe", cook, {"shopId": shop, "requestId": str(uuid4()), "orderId": data(order)["orderId"]})
    check("同一订单换请求号再出餐被拦 code 4", again[1].get("code") == 4, again)
    cancel = post("FnbKitchen/CancelManualOrder", cook, {"shopId": shop, "orderId": data(order)["orderId"]})
    check("已出餐订单取消被拦 code 4", cancel[1].get("code") == 4, cancel)
    detail = get("FnbKitchen/GetOrder", cook, shopId=shop, orderId=data(order)["orderId"])
    check("GetOrder 标记 served", ok(detail) and data(detail)["served"] is True, detail)
    pending = post("FnbKitchen/CreateManualOrder", cook, {"shopId": shop, "requestId": str(uuid4()), "tableNo": "B1",
                                                          "lines": [{"productId": data(bare)["productId"], "quantity": 1}]})
    check("无配方菜品建单 → pending", ok(pending) and data(pending)["reviewStatus"] == "pending", pending)
    check("员工核对订单返回 3", post("FnbKitchen/ReviewOrder", cook, {"shopId": shop, "orderId": data(pending)["orderId"]})[1].get("code") == 3)
    review = post("FnbKitchen/ReviewOrder", mgr, {"shopId": shop, "orderId": data(pending)["orderId"]})
    check("缺配方时店长核对被拒 code 1", review[1].get("code") == 1, review)
    check("待核对订单不能出餐预览", get("FnbKitchen/PreviewServe", cook, shopId=shop, orderId=data(pending)["orderId"])[1].get("code") == 1)
    cancelled = post("FnbKitchen/CancelManualOrder", cook, {"shopId": shop, "orderId": data(pending)["orderId"]})
    check("未出餐订单可取消", ok(cancelled), cancelled)
    big = post("FnbKitchen/CreateManualOrder", cook, {"shopId": shop, "requestId": str(uuid4()),
                                                      "lines": [{"productId": data(dish)["productId"], "quantity": 100}]})
    big_serve = post("FnbKitchen/PostServe", cook, {"shopId": shop, "requestId": str(uuid4()), "orderId": data(big)["orderId"]})
    shortage = {n["itemId"]: n for n in (data(big_serve) or {}).get("needs", [])}
    check("库存不足仍出餐并记欠料", ok(big_serve) and shortage[items["大白菜"]]["shortageQuantity"] > 0, big_serve)
    check("欠料后可用量为 0 不为负", stock_of(ctx, items["大白菜"])["availableQty"] == 0, stock_of(ctx, items["大白菜"]))

    print("\n[盘点]")
    receipt(ctx, cook, items["大白菜"], 2000)
    check("员工建盘点快照返回 3", post("FnbStocktake/CreateSnapshot", cook, {"shopId": shop, "requestId": str(uuid4()), "itemIds": [items["大白菜"]]})[1].get("code") == 3)
    snapshot = post("FnbStocktake/CreateSnapshot", mgr, {"shopId": shop, "requestId": str(uuid4()), "itemIds": [items["大白菜"]]})
    check("店长建盘点快照", ok(snapshot), snapshot)
    document_id = data(snapshot)["documentId"]
    rows = data(get("FnbStocktake/GetSnapshot", cook, shopId=shop, documentId=document_id))["rows"]
    check("快照系统数 = 2000g", rows[0]["system_qty"] == 2000, rows)
    counted = post("FnbStocktake/SaveCount", cook, {"shopId": shop, "documentId": document_id, "itemId": items["大白菜"],
                                                    "countedQuantity": 1700, "rowVersion": rows[0]["rowVersion"]})
    check("员工录入实盘 1700", ok(counted), counted)
    stale_count = post("FnbStocktake/SaveCount", cook, {"shopId": shop, "documentId": document_id, "itemId": items["大白菜"],
                                                        "countedQuantity": 1600, "rowVersion": rows[0]["rowVersion"]})
    check("旧 rowVersion 录入返回 4", stale_count[1].get("code") == 4, stale_count)
    adjust = get("FnbStocktake/PreviewAdjustment", cook, shopId=shop, documentId=document_id)
    check("差异预览 -300", ok(adjust) and data(adjust)[0]["differenceQuantity"] == -300, adjust)
    posted = post("FnbStocktake/PostStocktake", mgr, {"shopId": shop, "documentId": document_id, "gains": []})
    check("店长盘亏过账后可用 1700g", ok(posted) and stock_of(ctx, items["大白菜"])["availableQty"] == 1700, (posted, stock_of(ctx, items["大白菜"])))
    snap2 = data(post("FnbStocktake/CreateSnapshot", mgr, {"shopId": shop, "requestId": str(uuid4()), "itemIds": [items["大白菜"]]}))["documentId"]
    receipt(ctx, cook, items["大白菜"], 100)
    row2 = data(get("FnbStocktake/GetSnapshot", cook, shopId=shop, documentId=snap2))["rows"][0]
    post("FnbStocktake/SaveCount", cook, {"shopId": shop, "documentId": snap2, "itemId": items["大白菜"], "countedQuantity": 1800, "rowVersion": row2["rowVersion"]})
    changed = post("FnbStocktake/PostStocktake", mgr, {"shopId": shop, "documentId": snap2, "gains": []})
    check("盘点期间有入库 → 过账被拒 code 4", changed[1].get("code") == 4, changed)
    snap3 = data(post("FnbStocktake/CreateSnapshot", mgr, {"shopId": shop, "requestId": str(uuid4()), "itemIds": [items["大白菜"]]}))["documentId"]
    row3 = data(get("FnbStocktake/GetSnapshot", cook, shopId=shop, documentId=snap3))["rows"][0]
    post("FnbStocktake/SaveCount", cook, {"shopId": shop, "documentId": snap3, "itemId": items["大白菜"], "countedQuantity": row3["system_qty"] + 500, "rowVersion": row3["rowVersion"]})
    no_gain = post("FnbStocktake/PostStocktake", mgr, {"shopId": shop, "documentId": snap3, "gains": []})
    check("盘盈缺承接批次信息被拒 code 1", no_gain[1].get("code") == 1, no_gain)
    gain = post("FnbStocktake/PostStocktake", mgr, {"shopId": shop, "documentId": snap3, "gains": [
        {"itemId": items["大白菜"], "batchNo": "GAIN1", "expireDate": iso(TODAY + timedelta(days=3)), "storageType": "chilled",
         "unitCost": 0.004, "imageIds": [ctx["photos"][2]], "expiryNote": None}]})
    check("盘盈带承接批次过账", ok(gain), gain)

    print("\n[报表]")
    overview_cook = get("FnbReport/GetOverview", cook, shopId=shop)
    overview_mgr = get("FnbReport/GetOverview", mgr, shopId=shop)
    check("员工总览不含金额", ok(overview_cook) and all(r["total_amount"] is None for r in data(overview_cook)["rows"]), overview_cook)
    check("店长总览含金额", ok(overview_mgr) and any(r["total_amount"] is not None for r in data(overview_mgr)["rows"]), overview_mgr)
    check("员工损耗台账返回 3", get("FnbReport/GetLossLedger", cook, shopId=shop)[1].get("code") == 3)
    ledger = get("FnbReport/GetLossLedger", mgr, shopId=shop, **{"from": iso(TODAY - timedelta(days=7)), "to": iso(TODAY)})
    check("店长损耗台账含报损与盘亏", ok(ledger) and data(ledger)["total"] >= 2, ledger)

    print("\n[旧接口保护]")
    old = get("FnbMaterial/DisposeBatch", cook, id=sealed_batch, action="报废")
    check("旧 DisposeBatch 不能处置库存批次", old[0] == 200 and old[1].get("code") == 1 and "库存" in old[1].get("message", ""), old)
    gen = get("FnbMaterial/GenBatchNo", cook)
    check("旧 GenBatchNo 仍可为新入库发号", ok(gen) and data(gen)["batchNo"].startswith("B"), gen)

    print("\n[并发]")
    potato = items["土豆"]
    receipt(ctx, cook, potato, 1000)
    potato_dish = data(post("FnbRecipe/SaveDish", mgr, {"shopId": shop, "id": 0, "name": "土豆丝", "salePrice": 18, "categoryName": "热菜", "valid": True}))
    pd = data(post("FnbRecipe/SaveRecipeDraft", mgr, {"shopId": shop, "id": 0, "recipeType": "dish", "dishSpecId": potato_dish["specId"],
                                                      "outputQty": 1, "lines": [{"itemId": potato, "quantity": 300, "sort": 1}]}))
    post("FnbRecipe/PublishRecipe", mgr, {"shopId": shop, "recipeId": pd["id"], "rowVersion": pd["rowVersion"]})
    orders = [data(post("FnbKitchen/CreateManualOrder", cook, {"shopId": shop, "requestId": str(uuid4()),
                                                              "lines": [{"productId": potato_dish["productId"], "quantity": 1}]}))["orderId"]
              for _ in range(10)]
    with ThreadPoolExecutor(10) as pool:
        results = list(pool.map(lambda oid: post("FnbKitchen/PostServe", cook, {"shopId": shop, "requestId": str(uuid4()), "orderId": oid}), orders))
    statuses = sorted({(r[0], r[1].get("code")) for r in results})
    check("10 个并行出餐无 HTTP 500，失败只可能是 code 4", all(r[0] == 200 and r[1].get("code") in (0, 4) for r in results), statuses)
    succeeded = sum(1 for r in results if ok(r))
    check(f"并行出餐成功 {succeeded}/10，全部失败的应能重试", succeeded >= 1, statuses)
    retried = [post("FnbKitchen/PostServe", cook, {"shopId": shop, "requestId": str(uuid4()), "orderId": oid})
               for oid, r in zip(orders, results) if not ok(r)]
    check("失败的出餐重试后成功", all(ok(r) for r in retried), [r for r in retried if not ok(r)][:2])
    potato_stock = stock_of(ctx, potato)
    check("并发出餐后土豆库存 0 不为负", potato_stock["availableQty"] == 0 and potato_stock["totalQty"] >= 0, potato_stock)
    packs = data(receipt(ctx, cook, items["番茄酱"], 3, unit="ml", form="sealed", pack=1000, pack_name="瓶"))["batchId"]
    with ThreadPoolExecutor(8) as pool:
        opens = list(pool.map(lambda _: post("FnbInventory/PostOpen", cook, {"shopId": shop, "requestId": str(uuid4()), "parentBatchId": packs, "packCount": 1}), range(8)))
    opened_ok = sum(1 for r in opens if ok(r))
    check("8 个并行开封（3 瓶）无 HTTP 500", all(r[0] == 200 for r in opens), sorted({(r[0], r[1].get("code")) for r in opens}))
    check("并行开封至多成功 3 次", opened_ok <= 3, opened_ok)
    same = str(uuid4())
    with ThreadPoolExecutor(5) as pool:
        dup_receipts = list(pool.map(lambda _: receipt(ctx, cook, potato, 50, request_id=same), range(5)))
    check("同 requestId 并行入库 5 次无 HTTP 500", all(r[0] == 200 for r in dup_receipts), sorted({(r[0], r[1].get("code")) for r in dup_receipts}))
    docs = {data(r)["documentId"] for r in dup_receipts if ok(r)}
    check("同 requestId 并行入库只生成 1 张单", len(docs) <= 1, docs)


def main() -> int:
    build = subprocess.run(["dotnet", "build", "SnowmeetApi.csproj", "-v", "q", "-nologo"], cwd=base.API_DIR,
                           capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-2000:])
        return build.returncode
    with base.connect("master") as master:
        master.execute(f"CREATE DATABASE [{base.TEST_DATABASE}] COLLATE Chinese_PRC_CI_AS")
    process = None
    try:
        base.bootstrap()
        copy_session_tables()
        ctx = seed()
        workdir = Path(tempfile.mkdtemp(prefix="fnb_http_smoke_"))
        process = start_api(workdir)
        print(f"隔离库 {base.TEST_DATABASE}，本地 API {BASE_URL}，日志 {workdir / 'api.log'}", flush=True)
        scenarios(ctx)
    finally:
        if process is not None:
            process.terminate()
            process.wait(timeout=30)
        base.drop_database()
        print(f"已删除隔离测试库：{base.TEST_DATABASE}", flush=True)
    failed = [r for r in RESULTS if not r[0]]
    print(f"\n合计 {len(RESULTS)} 项，通过 {len(RESULTS) - len(failed)}，失败 {len(failed)}")
    for _, name, detail in failed:
        print(f"  ✗ {name}: {detail}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
