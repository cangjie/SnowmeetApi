using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Data;

// 重建 SQL 的结构来源。所有关系禁止级联；旧库存类型不属于 v4 EF 模型。
internal static class FnbSchemaConfiguration
{
    internal static void Configure(ModelBuilder m, bool sqlServer)
    {
        m.Ignore<FnbMaterialCategory>(); m.Ignore<FnbMaterialItem>(); m.Ignore<FnbMaterialBatchStock>();
        m.Ignore<FnbMaterialBatch>(); m.Ignore<FnbMaterialAlertLog>();
        m.Ignore<FnbMaterialStockView>(); m.Ignore<FnbMaterialLossView>();

        var unit = Table<FnbUnit>(m, "fnb_unit");
        unit.HasKey(x => x.code); Text(unit, "code", 32); Text(unit, "name", 40);
        unit.Property(x => x.factor_to_base).HasPrecision(18, 6);
        Check(unit, sqlServer, "unit", "dimension IN (1,2,3) AND factor_to_base > 0");

        var category = Table<FnbCategory>(m, "fnb_category");
        category.HasKey(x => x.id); Text(category, "name", 100); Text(category, "batch_code", 16);
        Text(category, "measure_type", 10); Text(category, "default_storage", 20);
        category.HasOne<FnbCategory>().WithMany().HasForeignKey(x => x.parent_id).OnDelete(DeleteBehavior.NoAction);
        category.HasIndex(x => new { x.parent_id, x.name }).IsUnique().HasFilter("[valid] = 1");
        category.HasIndex(x => x.batch_code).IsUnique().HasFilter("[valid] = 1 AND [level] = 2");
        Check(category, sqlServer, "tree", "(level = 1 AND parent_id IS NULL AND batch_code IS NULL AND measure_type IS NULL AND default_storage IS NULL AND warn_days IS NULL AND open_days IS NULL AND is_prepared = 0) OR (level = 2 AND parent_id IS NOT NULL AND batch_code IS NOT NULL AND measure_type IS NOT NULL AND default_storage IS NOT NULL AND measure_type IN ('weight','volume','count') AND default_storage IN ('ambient','chilled','frozen'))");
        Check(category, sqlServer, "days", "(warn_days IS NULL OR warn_days >= 0) AND (open_days IS NULL OR open_days >= 0)");

        var item = Table<FnbItem>(m, "fnb_item");
        item.HasKey(x => x.id); Text(item, "name", 200); Text(item, "item_type", 16); Text(item, "base_unit_code", 32);
        item.Property(x => x.low_stock_ratio).HasPrecision(5, 4); item.Property(x => x.low_stock_qty).HasPrecision(18, 6);
        item.HasOne<FnbCategory>().WithMany().HasForeignKey(x => x.category_id).OnDelete(DeleteBehavior.NoAction);
        item.HasOne<FnbUnit>().WithMany().HasForeignKey(x => x.base_unit_code).OnDelete(DeleteBehavior.NoAction);
        item.HasOne<UploadFile>().WithMany().HasForeignKey(x => x.image_id).OnDelete(DeleteBehavior.NoAction);
        item.HasIndex(x => new { x.category_id, x.name }).IsUnique().HasFilter("[valid] = 1");
        Check(item, sqlServer, "type", "item_type IN ('raw','prepared') AND base_unit_code IN ('g','ml','piece')");
        Check(item, sqlServer, "defaults", "(warn_days IS NULL OR warn_days >= 0) AND (open_days IS NULL OR open_days >= 0) AND (low_stock_ratio IS NULL OR (low_stock_ratio > 0 AND low_stock_ratio <= 1)) AND (low_stock_qty IS NULL OR low_stock_qty >= 0) AND (low_stock_ratio IS NULL OR low_stock_qty IS NULL)");

        var rule = Table<FnbShelfLifeRule>(m, "fnb_shelf_life_rule");
        rule.HasKey(x => x.id); Text(rule, "storage_type", 20); Text(rule, "season", 8);
        rule.HasOne<FnbCategory>().WithMany().HasForeignKey(x => x.category_id).OnDelete(DeleteBehavior.NoAction);
        rule.HasOne<FnbItem>().WithMany().HasForeignKey(x => x.item_id).OnDelete(DeleteBehavior.NoAction);
        rule.HasIndex(x => new { x.category_id, x.storage_type, x.season }).IsUnique().HasFilter("[valid] = 1 AND [category_id] IS NOT NULL");
        rule.HasIndex(x => new { x.item_id, x.storage_type, x.season }).IsUnique().HasFilter("[valid] = 1 AND [item_id] IS NOT NULL");
        Check(rule, sqlServer, "owner", "(category_id IS NULL AND item_id IS NOT NULL) OR (category_id IS NOT NULL AND item_id IS NULL)");
        Check(rule, sqlServer, "value", "season IN ('all','warm','cold') AND days > 0 AND storage_type IN ('ambient','chilled','frozen')");

        var form = Table<FnbItemForm>(m, "fnb_item_form");
        form.HasKey(x => x.id); form.HasAlternateKey(x => new { x.id, x.item_id });
        Text(form, "name", 100); Text(form, "unit_name", 40); Text(form, "storage_type", 20);
        Text(form, "form_code", 16); Text(form, "in_op_name", 100);
        form.HasOne<FnbItem>().WithMany().HasForeignKey(x => x.item_id).OnDelete(DeleteBehavior.NoAction);
        form.HasIndex(x => new { x.item_id, x.seq }).IsUnique().HasFilter("[valid] = 1");
        form.HasIndex(x => new { x.item_id, x.form_code }).IsUnique().HasFilter("[valid] = 1");
        Check(form, sqlServer, "value", "seq >= 0 AND per_base > 0 AND storage_type IN ('ambient','chilled','frozen') AND (shelf_after_op_days IS NULL OR shelf_after_op_days >= 0)");
        Check(form, sqlServer, "operation", "(seq = 0 AND in_op_name IS NULL AND in_op_ratio IS NULL AND in_op_yield IS NULL AND in_op_hours IS NULL) OR (seq > 0 AND in_op_name IS NOT NULL AND in_op_ratio IS NOT NULL AND in_op_ratio > 0 AND in_op_yield IS NOT NULL AND in_op_yield > 0 AND in_op_yield <= 1 AND in_op_hours IS NOT NULL AND in_op_hours >= 0)");

        var spec = Table<FnbPurchaseSpec>(m, "fnb_purchase_spec");
        spec.HasKey(x => x.id); spec.HasAlternateKey(x => new { x.id, x.item_id });
        Text(spec, "name", 100); Text(spec, "brand", 100); Text(spec, "pack_desc", 200); Text(spec, "barcode", 100);
        spec.HasOne<FnbItem>().WithMany().HasForeignKey(x => x.item_id).OnDelete(DeleteBehavior.NoAction);
        spec.HasOne<FnbItemForm>().WithMany().HasForeignKey(x => new { x.entry_form_id, x.item_id })
            .HasPrincipalKey(x => new { x.id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        spec.HasIndex(x => x.barcode).IsUnique().HasFilter("[valid] = 1 AND [barcode] IS NOT NULL");

        var batch = Table<FnbBatch>(m, "fnb_batch");
        batch.HasKey(x => x.id); batch.HasAlternateKey(x => new { x.id, x.shop_id, x.item_id });
        Text(batch, "batch_no", 100); Text(batch, "state", 12); Text(batch, "pack_label", 40);
        Text(batch, "storage_type", 20); Text(batch, "expiry_source", 24); Text(batch, "dispose_status", 20);
        batch.HasOne<Shop>().WithMany().HasForeignKey(x => x.shop_id).OnDelete(DeleteBehavior.NoAction);
        batch.HasOne<FnbItemForm>().WithMany().HasForeignKey(x => new { x.form_id, x.item_id })
            .HasPrincipalKey(x => new { x.id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        batch.HasOne<FnbPurchaseSpec>().WithMany().HasForeignKey(x => new { x.spec_id, x.item_id })
            .HasPrincipalKey(x => new { x.id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        batch.HasOne<FnbBatch>().WithMany().HasForeignKey(x => new { x.parent_batch_id, x.shop_id, x.item_id })
            .HasPrincipalKey(x => new { x.id, x.shop_id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        batch.HasIndex(x => new { x.shop_id, x.batch_no }).IsUnique();
        batch.HasIndex(x => new { x.shop_id, x.item_id, x.form_id });
        foreach (var p in new[] { "production_date", "expire_date", "op_date", "op_expire_date", "effective_expire" }) batch.Property(p).HasColumnType("date");
        if (sqlServer) batch.Property(x => x.effective_expire).HasComputedColumnSql("CASE WHEN [op_expire_date] IS NOT NULL AND [op_expire_date] < [expire_date] THEN [op_expire_date] ELSE [expire_date] END", stored: true);
        Check(batch, sqlServer, "value", "state IN ('sealed','staged','final') AND quantity >= 0 AND amount >= 0 AND storage_type IN ('ambient','chilled','frozen')");
        Check(batch, sqlServer, "pack", "(state = 'sealed' AND pack_size IS NOT NULL AND pack_size > 0 AND pack_label IS NOT NULL AND quantity % pack_size = 0) OR (state <> 'sealed' AND pack_size IS NULL AND pack_label IS NULL)");
        Check(batch, sqlServer, "disposal", "dispose_status IS NULL OR (dispose_status IN ('used_up','wasted','destroyed') AND quantity = 0 AND amount = 0)");
        Check(batch, sqlServer, "dates", "(production_date IS NULL OR production_date <= expire_date) AND (op_expire_date IS NULL OR op_date IS NOT NULL)");

        var image = Table<FnbBatchImage>(m, "fnb_batch_image"); image.HasKey(x => new { x.batch_id, x.upload_id });
        image.HasOne<FnbBatch>().WithMany().HasForeignKey(x => x.batch_id).OnDelete(DeleteBehavior.NoAction);
        image.HasOne<UploadFile>().WithMany().HasForeignKey(x => x.upload_id).OnDelete(DeleteBehavior.NoAction);

        var dish = Table<FnbDishSpec>(m, "fnb_dish_spec"); dish.HasKey(x => x.id);
        Text(dish, "name", 100); Text(dish, "spec_code", 64);
        dish.HasOne<Product>().WithMany().HasForeignKey(x => x.product_id).OnDelete(DeleteBehavior.NoAction);
        dish.HasOne<Shop>().WithMany().HasForeignKey(x => x.shop_id).OnDelete(DeleteBehavior.NoAction);
        dish.HasIndex(x => new { x.product_id, x.name }).IsUnique().HasFilter("[valid] = 1");

        var recipe = Table<FnbRecipe>(m, "fnb_recipe"); recipe.HasKey(x => x.id);
        Text(recipe, "recipe_type", 20); Text(recipe, "status", 20); Text(recipe, "remark", 1000);
        recipe.HasOne<Shop>().WithMany().HasForeignKey(x => x.shop_id).OnDelete(DeleteBehavior.NoAction);
        recipe.HasOne<FnbItem>().WithMany().HasForeignKey(x => x.output_item_id).OnDelete(DeleteBehavior.NoAction);
        recipe.HasOne<FnbDishSpec>().WithMany().HasForeignKey(x => x.dish_spec_id).OnDelete(DeleteBehavior.NoAction);
        recipe.HasIndex(x => new { x.shop_id, x.output_item_id, x.version_no }).IsUnique().HasFilter("[output_item_id] IS NOT NULL");
        recipe.HasIndex(x => new { x.shop_id, x.dish_spec_id, x.version_no }).IsUnique().HasFilter("[dish_spec_id] IS NOT NULL");
        Check(recipe, sqlServer, "owner", "((recipe_type = 'prep' AND output_item_id IS NOT NULL AND dish_spec_id IS NULL) OR (recipe_type = 'dish' AND dish_spec_id IS NOT NULL AND output_item_id IS NULL)) AND output_qty > 0 AND version_no > 0 AND status IN ('draft','published','retired')");
        var recipeLine = Table<FnbRecipeLine>(m, "fnb_recipe_line"); recipeLine.HasKey(x => x.id); Text(recipeLine, "remark", 600);
        recipeLine.HasOne<FnbRecipe>().WithMany().HasForeignKey(x => x.recipe_id).OnDelete(DeleteBehavior.NoAction);
        recipeLine.HasOne<FnbItem>().WithMany().HasForeignKey(x => x.item_id).OnDelete(DeleteBehavior.NoAction);
        recipeLine.HasIndex(x => new { x.recipe_id, x.item_id }).IsUnique();
        Check(recipeLine, sqlServer, "quantity", "quantity > 0");

        var order = Table<FnbOrder>(m, "fnb_order"); order.HasKey(x => x.id);
        Text(order, "source_type", 20); Text(order, "external_order_no", 256); Text(order, "display_no", 128);
        Text(order, "table_no", 100); Text(order, "order_status", 24); Text(order, "platform_status", 100);
        Text(order, "refund_status", 20); Text(order, "review_status", 20); Text(order, "remark", 2000);
        order.Property(x => x.business_date).HasColumnType("date");
        order.HasOne<Shop>().WithMany().HasForeignKey(x => x.shop_id).OnDelete(DeleteBehavior.NoAction);
        order.HasIndex(x => new { x.shop_id, x.source_type, x.external_order_no }).IsUnique().HasFilter("[external_order_no] IS NOT NULL");
        var orderLine = Table<FnbOrderLine>(m, "fnb_order_line"); orderLine.HasKey(x => x.id);
        Text(orderLine, "line_key", 256); Text(orderLine, "option_key", 256); Text(orderLine, "item_name", 300);
        Text(orderLine, "spec_name", 200); Text(orderLine, "options_text", 2000); Text(orderLine, "remark", 1000);
        orderLine.HasOne<FnbOrder>().WithMany().HasForeignKey(x => x.order_id).OnDelete(DeleteBehavior.NoAction);
        orderLine.HasOne<FnbDishSpec>().WithMany().HasForeignKey(x => x.dish_spec_id).OnDelete(DeleteBehavior.NoAction);
        orderLine.HasOne<FnbRecipe>().WithMany().HasForeignKey(x => x.recipe_id).OnDelete(DeleteBehavior.NoAction);
        orderLine.HasIndex(x => new { x.order_id, x.line_key, x.option_key }).IsUnique();
        var import = Table<FnbOrderImport>(m, "fnb_order_import"); import.HasKey(x => x.id);
        Text(import, "source_method", 20); Text(import, "dedupe_key", 256); Text(import, "process_status", 24);
        import.HasOne<FnbOrder>().WithMany().HasForeignKey(x => x.order_id).OnDelete(DeleteBehavior.NoAction);
        import.HasIndex(x => new { x.shop_id, x.source_method, x.dedupe_key }).IsUnique();

        var document = Table<FnbStockDocument>(m, "fnb_stock_document"); document.HasKey(x => x.id);
        Text(document, "document_no", 80); Text(document, "document_type", 32); Text(document, "status", 20);
        Text(document, "source_client", 20); Text(document, "reason_code", 32); Text(document, "reference_no", 200); Text(document, "remark", 2000);
        document.Property(x => x.business_date).HasColumnType("date");
        document.HasOne<Shop>().WithMany().HasForeignKey(x => x.shop_id).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<FnbOrder>().WithMany().HasForeignKey(x => x.order_id).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<FnbRecipe>().WithMany().HasForeignKey(x => x.recipe_id).OnDelete(DeleteBehavior.NoAction);
        document.HasIndex(x => new { x.shop_id, x.document_type, x.request_id }).IsUnique();
        document.HasIndex(x => new { x.shop_id, x.document_no }).IsUnique();
        Check(document, sqlServer, "type", "document_type IN ('receipt','op','prep','serve','waste','destroy','stocktake') AND source_client IN ('mini','wecom') AND status IN ('draft','posted','cancelled')");
        var line = Table<FnbStockDocumentLine>(m, "fnb_stock_document_line"); line.HasKey(x => x.id);
        Text(line, "item_name", 200); Text(line, "input_unit_name", 40); Text(line, "remark", 1000);
        line.HasOne<FnbStockDocument>().WithMany().HasForeignKey(x => x.document_id).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<FnbItem>().WithMany().HasForeignKey(x => x.item_id).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<FnbPurchaseSpec>().WithMany().HasForeignKey(x => new { x.spec_id, x.item_id }).HasPrincipalKey(x => new { x.id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<FnbItemForm>().WithMany().HasForeignKey(x => new { x.form_id, x.item_id }).HasPrincipalKey(x => new { x.id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<FnbBatch>().WithMany().HasForeignKey(x => new { x.specified_batch_id, x.shop_id, x.item_id }).HasPrincipalKey(x => new { x.id, x.shop_id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        line.HasIndex(x => new { x.document_id, x.line_no }).IsUnique();
        if (sqlServer) { line.Property(x => x.planned_qty).HasComputedColumnSql("CONVERT(decimal(19,6),[input_qty] * [input_to_base])", true); line.Property(x => x.shortage_qty).HasComputedColumnSql("CONVERT(decimal(19,6),CASE WHEN [input_qty]*[input_to_base] > [actual_qty] THEN [input_qty]*[input_to_base]-[actual_qty] ELSE 0 END)", true); }
        Check(line, sqlServer, "quantity", "direction IN (-1,1) AND input_qty >= 0 AND input_to_base > 0 AND actual_qty >= 0 AND actual_amount >= 0");
        var movement = Table<FnbStockMovement>(m, "fnb_stock_movement"); movement.HasKey(x => x.id);
        movement.HasOne<FnbStockDocumentLine>().WithMany().HasForeignKey(x => x.document_line_id).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<FnbBatch>().WithMany().HasForeignKey(x => new { x.batch_id, x.shop_id, x.item_id }).HasPrincipalKey(x => new { x.id, x.shop_id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        if (sqlServer) { movement.Property(x => x.delta_qty).HasComputedColumnSql("CONVERT(decimal(19,6),[direction] * [quantity])", true); movement.Property(x => x.delta_amount).HasComputedColumnSql("CONVERT(decimal(19,6),[direction] * [amount])", true); }
        Check(movement, sqlServer, "quantity", "direction IN (-1,1) AND quantity >= 0 AND amount >= 0 AND balance_qty >= 0 AND balance_amount >= 0");
        var count = Table<FnbStocktakeLine>(m, "fnb_stocktake_line"); count.HasKey(x => x.id); Text(count, "remark", 1000);
        count.HasOne<FnbStockDocument>().WithMany().HasForeignKey(x => x.document_id).OnDelete(DeleteBehavior.NoAction);
        count.HasOne<FnbItem>().WithMany().HasForeignKey(x => x.item_id).OnDelete(DeleteBehavior.NoAction);
        count.HasIndex(x => new { x.document_id, x.item_id }).IsUnique();
        if (sqlServer) count.Property(x => x.difference_qty).HasComputedColumnSql("CONVERT(decimal(19,6),[counted_qty] - [system_qty])", true);
        Check(count, sqlServer, "quantity", "system_qty >= 0 AND (counted_qty IS NULL OR counted_qty >= 0)");

        var operation = Table<FnbStockOperation>(m, "fnb_stock_operation"); operation.HasKey(x => x.id);
        Text(operation, "op_name", 100); Text(operation, "status", 16);
        operation.HasOne<FnbStockDocument>().WithMany().HasForeignKey(x => x.document_id).OnDelete(DeleteBehavior.NoAction);
        operation.HasOne<FnbItemForm>().WithMany().HasForeignKey(x => new { x.from_form_id, x.item_id }).HasPrincipalKey(x => new { x.id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        operation.HasOne<FnbItemForm>().WithMany().HasForeignKey(x => new { x.to_form_id, x.item_id }).HasPrincipalKey(x => new { x.id, x.item_id }).OnDelete(DeleteBehavior.NoAction);
        operation.HasOne<FnbBatch>().WithMany().HasForeignKey(x => x.source_batch_id).OnDelete(DeleteBehavior.NoAction);
        operation.HasOne<FnbBatch>().WithMany().HasForeignKey(x => x.output_batch_id).OnDelete(DeleteBehavior.NoAction);
        operation.HasOne<Staff>().WithMany().HasForeignKey(x => x.staff_id).OnDelete(DeleteBehavior.NoAction);
        operation.HasIndex(x => x.document_id).IsUnique();
        Check(operation, sqlServer, "value", "input_qty > 0 AND std_ratio > 0 AND std_yield > 0 AND std_yield <= 1 AND expected_qty >= 0 AND actual_qty >= 0 AND loss_base_qty >= 0 AND duration_hours >= 0 AND status IN ('running','done')");

        m.Entity<FnbStockView>(e => { e.HasNoKey(); e.ToView("vw_fnb_v4_stock"); });
        m.Entity<FnbLossView>(e => { e.HasNoKey(); e.ToView("vw_fnb_v4_loss"); e.Property(x => x.business_date).HasColumnType("date"); });
        foreach (var entity in m.Model.GetEntityTypes().Where(x => (x.GetTableName() ?? "").StartsWith("fnb_") || (x.GetViewName() ?? "").StartsWith("vw_fnb_")))
        {
            foreach (var p in entity.GetProperties().ToArray())
            {
                var b = m.Entity(entity.ClrType).Property(p.Name);
                if ((p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)) && p.GetPrecision() == null) b.HasPrecision(19, 6);
                if (sqlServer && p.ClrType == typeof(string)) b.HasColumnType($"varchar({p.GetMaxLength() ?? 200})").UseCollation("Chinese_PRC_CI_AS");
                if (sqlServer && (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) && p.GetColumnType() != "date") b.HasColumnType("datetime2(3)");
                if (p.Name == "valid") b.HasDefaultValue(true).HasSentinel(true);
                if (p.Name == "row_version") b.IsRowVersion();
            }
        }
    }

    private static EntityTypeBuilder<T> Table<T>(ModelBuilder m, string name) where T : class
    { var e = m.Entity<T>(); e.ToTable(name == "fnb_unit" ? name : name.Replace("fnb_", "fnb_v4_")); return e; }
    private static void Text<T>(EntityTypeBuilder<T> e, string name, int size) where T : class => e.Property<string>(name).HasMaxLength(size).IsUnicode(false);
    private static void Check<T>(EntityTypeBuilder<T> e, bool sql, string name, string expression) where T : class
    { if (sql) e.ToTable(t => t.HasCheckConstraint("CK_" + e.Metadata.GetTableName() + "_" + name, expression)); }
}
