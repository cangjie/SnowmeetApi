# 美团管家采集程序（Python）

用一个**保留登录状态的真实浏览器窗口**打开美团管家（pos.meituan.com），由页面自己请求数据。程序只截获返回结果，存成本地文件。

能抓到的内容：
- 店内、外卖平台、外卖自营的当天全部订单（含取消、退单）
- 每张单的菜品明细和退款明细
- 每天一份菜品档案（221 道菜的规格、价格、套餐组成、分类）

接口与字段说明见 `snowmeet_ai_doc/docs/fnb/2026-10-03-meituan-pos-order-api.md`。

- **适用电脑：** 有桌面、能弹出浏览器窗口的 Windows 或 macOS。
- **登录：** 验证码、滑块都由人在窗口里自己完成，程序不代填、不破解。
- **出口 IP：** 登录时需要中国大陆 IP。

## 安装

需要 Python 3.10 及以上，电脑上装有 Edge 或 Chrome。

Windows：

```bash
py -m venv .venv
```

```bash
.venv\Scripts\python -m pip install -r requirements.txt
```

macOS：

```bash
python3 -m venv .venv
```

```bash
.venv/bin/python -m pip install -r requirements.txt
```

不需要执行 `playwright install`，程序直接用电脑上已有的 Edge 或 Chrome。

## 配置

把 `collector.example.json` 复制成 `collector.json`，再按需修改：

| 配置项 | 说明 |
|---|---|
| `phone` | 登录页预填的手机号 |
| `browser_channel` | `msedge` 或 `chrome` |
| `direct_interface` | **只在电脑开着全局 VPN 时才填**，填本地宽带网卡的名字（Windows 如 `Wi-Fi`，macOS 如 `en0`），让浏览器绕开 VPN、用国内 IP 出网。门店电脑不开 VPN，保持 `null` 即可 |
| `interval_minutes`、`active_hours`、`idle_interval_minutes` | 抓取间隔：营业时段内每几分钟抓一轮，其余时间每几分钟抓一轮 |
| `server_url`、`server_token` | 心跳上报，见下文「企业微信提醒」。`server_token` 找管理员要，留空就没有提醒 |

`collector.json`、`profile/`（登录状态）、`out/`（抓到的数据）、`.venv/` 都不进 git。

## 运行

```bash
.venv\Scripts\python -m meituan_collector serve
```

- **常驻运行（部署就用这条）：**
  - 浏览器窗口一直开着；
  - 营业时段默认 06:00–24:00，每 3 分钟抓一轮，其余时间每 60 分钟一轮；
  - 菜品档案每天抓一次。
- **第一次运行：** 窗口停在登录页，手机号已经填好。自己获取并输入验证码；出现滑块也请手动完成。之后登录状态一直保存，重启电脑也不用再登。
- **不要关这个浏览器窗口。**关掉后程序会退出（退出码非 0），由下面配置的自动启动把它重新拉起来。

其它命令（在常驻进程开着时，它们都连到同一个浏览器，不会另开窗口）：

| 命令 | 用途 |
|---|---|
| `python -m meituan_collector once` | 立即抓一轮；加 `--dishes` 强制重抓菜品档案 |
| `python -m meituan_collector probe` | 自检：网络、出口 IP、心跳服务器、浏览器出口 IP、登录状态。结果是一段文字，发给开发人员即可 |
| `python -m meituan_collector cmd pages` | 列出浏览器标签页。还有 goto、click、click-css、fill-css、press、text、shot、eval、frames 等命令，用来勘察页面 |
| `python -m meituan_collector login` | 只打开窗口等人登录 |
| `python -m meituan_collector record` | 录制：把页面发出的所有接口返回存到 `out/record/`，用于勘察新页面 |

## 企业微信提醒（登录失效、电脑停机）

企业微信接口有 IP 白名单，门店电脑发不了消息。所以常驻程序只**每 5 分钟向服务器（SnowmeetApi）上报一次心跳**，状态一变（比如登录失效）就立即上报；提醒由服务器发。心跳只含运行状态（ok / 等登录 / 出错、距上一轮多久、待补抓几天、机器名），不含订单和顾客信息。

| 情况 | 服务器怎么提醒 |
|---|---|
| 美团管家登录失效，程序在等人登录 | 立即提醒；没恢复每 2 小时再提醒一次；重新登录后发「已恢复」 |
| 超过 20 分钟收不到心跳（关机、断网、程序退出） | 提醒一次；没恢复每 6 小时再提醒；恢复上报后发「已恢复」，说明中断了多久 |
| 心跳正常，但超过 2 小时没完成一轮抓取（程序卡住） | 提醒；没恢复每 6 小时再提醒 |

- 收到登录失效提醒后，到采集电脑上，在美团管家窗口里用验证码登录（验证码自己输，滑块自己拖）。
- 停机、等登录期间的订单不会丢，恢复后程序会自动补抓（见下一节）。
- 服务器上的两份配置（工作目录下，不进 git）：
  - `config.meituanCollectorToken`：一行随机字符串，与本机 `collector.json` 的 `server_token` 相同；
  - `config.meituanCollectorNotify`：接收人的企业微信 UserId，多人用 `|` 分隔。
- 检查是否通：运行 `probe`，看「心跳服务器」那一行。
- 电脑开着全局 VPN 时，心跳也从 `direct_interface` 指定的网卡发出。

## 开始日期与停机补抓

**`start_date`（开始日期）**
- 设成想从哪个营业日开始抓，例如雪季第一天 `"2025-11-01"`。
- 留空（`null`）就从程序第一次运行那天开始，不抓更早的。
- 以后改早也可以，改完重启程序，更早的日子会自动补上。

**完整的日子**
某个营业日同时满足以下条件，就记为「完整」，写进 `out/coverage.json`：
- 那天已经过去；
- 当天的订单列表、每张单的详情、退款单列表都抓全了。

**停机后补抓**
电脑坏了、停电、关机几天都没关系。重新开机后，从 `start_date` 到前天之间凡是不完整的日子，程序会**按从早到晚**一天一天补抓：
- 每轮最多打开 `backfill_details_per_round` 张详情，默认 20，避免补抓耽误当天的抓取。没补完的下一轮继续。
- 有待补的日子时，夜间也按 3 分钟一轮跑，尽快补完。按默认设置每小时约补 400 张单。
- 日志里每轮都会写「补抓 某天……」「还剩 N 张详情」「还有 N 天待补抓」。

**补抓的几点限制**
- 补抓到的是订单**现在**的样子。停机期间发生过的退款、取消会直接体现在订单里，但看不到「什么时候变的」。
- 菜品档案只能抓到当前这一份，停机那几天的菜品档案补不回来。
- 补抓过的日子不会再定期复查。如果之后有新退款，还是会通过当天的退款单列表被发现，原订单会重新抓取。
- 美团报表能往回查多久没有实测过。目前只验证到往前一周（跨月也能选）。如果要补很久以前的日子，先小范围试一下。

## 检查范围与重复抓取

**检查范围**
- 当天的订单：每轮都查。
- 昨天的订单和退款：默认每小时查一次（`previous_day_check_minutes`），这样过了零点也能接住昨天订单的退款、取消。
- 前天及更早的订单不再检查。例外：今天或昨天的「退款单明细」里出现了更早订单的退款，会把那张原订单补抓下来。

**每轮做的事**
1. 订单列表切到「全部订单」，取消单、退单都会出现。
   - 列表里单独出现的退款单行（单号 88 开头、金额为负）没有订单详情，程序按退款单详情去取，汇总表备注写「退款单」。
2. 判断要不要打开详情：
   - 列表上的状态、金额、退款字段没变 → 不重新打开详情；
   - 新单或这些字段有变化 → 打开订单详情，有退款再打开退款单详情；
   - 每 2 小时（`full_refresh_minutes`）不管变没变都重抓一遍，兜底。
3. 打开「退款单明细」：凡是有新退款、但本地订单里还没有这笔的，重新抓那张原订单。
   - 店内部分退款后，原订单的状态和金额都不变，只能靠这一步发现。

**同一张单只有一个文件**
- 文件名是「类型_完整订单号」，重复抓到就覆盖，不会产生重复。
- 汇总表每轮整张重写，一张单一行。

**变化标志**
内容和上次保存的不一样时（状态、金额、菜品、支付、优惠、退款），程序会：
- 在订单文件里写入 `changed: true`、`version`（第几版）和 `changes`（每次变化的时间和内容，例如「状态 已结账→已撤单」「新增退款 7.00（乌苏 红×1.0），原因：错点」「加菜 拿铁×1.0」）；
- 把旧版本保存到 `orders/营业日/history/类型_订单号_v1.json`；
- 在 `changes_YYYY-MM-DD.csv` 里记一行，日期是发现变化的那天；
- 在汇总表里显示「版本」和「最后变化」。

**和手动操作互不干扰**
常驻抓取、`once`、`cmd` 共用一个浏览器，用 `out/round.lock`、`out/cmd.lock` 两个锁文件协调：
- 正在抓取时，`cmd` 会等这一轮结束再操作；
- 刚用过 `cmd` 的 3 分钟内，常驻程序不开始新的一轮。

## 抓到的文件（`out/`）

| 位置 | 内容 |
|---|---|
| `orders/营业日/{店内｜外卖｜外卖自营}_{订单号}.json` | 每张单一份：列表数据 `list`、详情 `detail`（含菜品明细 `itemList`）、退款单详情 `refunds`、版本与变化记录 |
| `orders/营业日/history/` | 内容变化前的旧版本 |
| `summary_YYYY-MM-DD.csv` | 每个营业日一张汇总表（今天每轮重写，昨天每小时重写），可直接用 Excel 打开，和管家页面对数 |
| `changes_YYYY-MM-DD.csv` | 当天发现的订单变化 |
| `coverage.json` | 开始日期，以及哪些营业日已经抓完整（补抓进度） |
| `dishes/catalog_YYYY-MM-DD.json` | 菜品档案 |
| `logs/` | 日志 |
| `debug/` | 出错时自动保存的截图和现场记录 |
| `probe/` | 自检报告 |

外卖收货人的姓名、电话、地址，以及文本里的手机号，保存前都已替换成 `***`。

## 开机自动启动

### Windows（计划任务）

用要运行采集的那个 Windows 账号，在 PowerShell 里执行（把路径换成实际位置）：

```powershell
$d = 'C:\meituan_collector'
$a = New-ScheduledTaskAction -Execute "$d\.venv\Scripts\pythonw.exe" -Argument '-m meituan_collector serve' -WorkingDirectory $d
$t = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$s = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit 0 -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName MeituanCollector -Action $a -Trigger $t -Settings $s
```

- 这样设置后，用户登录 Windows 时启动，异常退出 1 分钟后自动重启。
- 用 `pythonw.exe` 运行不会出现命令行窗口，日志只写进 `out/logs/`。
- 不要做成 Windows 服务：服务没有桌面，浏览器窗口出不来，登录失效时没人能处理。
- 电脑要设成开机自动登录 Windows、不睡眠。

### macOS（launchd）

在 `~/Library/LaunchAgents/com.snowmeet.meituan-collector.plist` 写入以下内容（把路径换成实际位置），然后运行 `launchctl load ~/Library/LaunchAgents/com.snowmeet.meituan-collector.plist`。

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>com.snowmeet.meituan-collector</string>
  <key>ProgramArguments</key><array>
    <string>/Users/你/meituan_collector/.venv/bin/python</string>
    <string>-m</string><string>meituan_collector</string><string>serve</string>
  </array>
  <key>WorkingDirectory</key><string>/Users/你/meituan_collector</string>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
</dict></plist>
```

- LaunchAgent 在用户登录后运行，能弹出浏览器窗口；`KeepAlive` 负责退出后自动重启。
- Mac 要设成自动登录、不睡眠。

## 升级

1. 停掉自动启动（Windows 在「任务计划程序」里结束 MeituanCollector 任务；Mac 运行 `launchctl unload`）。浏览器会随之关闭。
2. 替换代码文件。
3. 重新启动。

`collector.json`、`profile/`、`out/` 保留不动，就不用重新登录。

## 测试

```bash
.venv\Scripts\python -m unittest discover -s tests -t .
```
