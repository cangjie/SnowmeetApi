// SQL Server mappings for the applied food inventory schema.
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Data;

internal static class FnbSchemaConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FnbMaterialStockView>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_fnb_material_stock");
        });
        modelBuilder.Entity<FnbMaterialLossView>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_fnb_material_loss");
            e.Property(x => x.business_date).HasColumnType("date");
        });
        modelBuilder.Entity<FnbUnit>(e =>
        {
            e.ToTable("fnb_unit");
            e.HasKey(x => x.code);
            e.Property(x => x.code).HasColumnType("varchar(32)");
            e.Property(x => x.name).HasColumnType("varchar(40)");
            e.Property(x => x.factor_to_base).HasPrecision(18, 6);
            e.Property(x => x.valid).HasDefaultValue(true);
            e.Property(x => x.sort).HasDefaultValue(0);
        });

        modelBuilder.Entity<FnbMaterialCategory>(e =>
        {
            e.ToTable("fnb_material_category");
            e.HasKey(x => x.id);
            e.Property(x => x.name).HasColumnType("varchar(100)");
            e.Property(x => x.default_storage).HasColumnType("varchar(20)");
            e.Property(x => x.sort).HasDefaultValue(0);
            e.Property(x => x.valid).HasDefaultValue(true);
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<FnbShelfLifeRule>(e =>
        {
            e.ToTable("fnb_shelf_life_rule");
            e.HasKey(x => x.id);
            e.Property(x => x.storage_type).HasColumnType("varchar(20)");
            e.Property(x => x.shelf_life_unit).HasColumnType("varchar(10)");
            e.Property(x => x.remark).HasColumnType("varchar(600)");
            e.Property(x => x.valid).HasDefaultValue(true);
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<FnbMaterialItem>(e =>
        {
            e.ToTable("fnb_material_item");
            e.HasKey(x => x.id);
            e.Property(x => x.code).HasColumnType("varchar(64)");
            e.Property(x => x.name).HasColumnType("varchar(200)");
            e.Property(x => x.item_type).HasColumnType("varchar(32)");
            e.Property(x => x.base_unit_code).HasColumnType("varchar(32)");
            e.Property(x => x.default_input_unit_code).HasColumnType("varchar(32)");
            e.Property(x => x.default_open_storage).HasColumnType("varchar(20)");
            e.Property(x => x.low_stock_ratio).HasPrecision(5, 4);
            e.Property(x => x.low_stock_qty).HasPrecision(18, 6);
            e.Property(x => x.remark).HasColumnType("varchar(1000)");
            e.Property(x => x.valid).HasDefaultValue(true);
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<FnbMaterialBatchStock>(e =>
        {
            e.ToTable("fnb_material_batch_stock");
            e.HasKey(x => x.batch_id);
            // The old expiry row owns this key; it is not an IDENTITY column.
            e.Property(x => x.batch_id).ValueGeneratedNever();
            e.Property(x => x.stock_form).HasColumnType("varchar(20)");
            e.Property(x => x.storage_type).HasColumnType("varchar(20)");
            e.Property(x => x.storage_location).HasColumnType("varchar(200)");
            e.Property(x => x.quantity).HasPrecision(18, 6);
            e.Property(x => x.quantity).HasDefaultValue(0m);
            e.Property(x => x.stock_amount).HasPrecision(19, 6);
            e.Property(x => x.stock_amount).HasDefaultValue(0m);
            e.Property(x => x.pack_size).HasPrecision(18, 6);
            e.Property(x => x.pack_unit_name).HasColumnType("varchar(40)");
            e.Property(x => x.sealed_pack_count).ValueGeneratedOnAddOrUpdate();
            e.Property(x => x.opened_date).HasColumnType("date");
            e.Property(x => x.original_expire_date).HasColumnType("date");
            e.Property(x => x.opened_expire_date).HasColumnType("date");
            e.Property(x => x.open_storage_type).HasColumnType("varchar(20)");
            e.Property(x => x.calculated_expire_date).HasColumnType("date");
            e.Property(x => x.expiry_source).HasColumnType("varchar(24)");
            e.Property(x => x.expiry_note).HasColumnType("varchar(1000)");
            e.Property(x => x.is_destroyed).HasDefaultValue(false);
            e.Property(x => x.received_at).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.row_version).IsRowVersion();
        });

        modelBuilder.Entity<FnbDishSpec>(e =>
        {
            e.ToTable("fnb_dish_spec");
            e.HasKey(x => x.id);
            e.Property(x => x.spec_code).HasColumnType("varchar(64)");
            e.Property(x => x.name).HasColumnType("varchar(100)");
            e.Property(x => x.sale_price).HasPrecision(19, 4);
            e.Property(x => x.is_default).HasDefaultValue(false);
            e.Property(x => x.valid).HasDefaultValue(true);
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<FnbRecipe>(e =>
        {
            e.ToTable("fnb_recipe");
            e.HasKey(x => x.id);
            e.Property(x => x.recipe_type).HasColumnType("varchar(20)");
            e.Property(x => x.output_qty).HasPrecision(18, 6);
            e.Property(x => x.status).HasColumnType("varchar(20)");
            e.Property(x => x.status).HasDefaultValue("draft");
            e.Property(x => x.remark).HasColumnType("varchar(1000)");
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.row_version).IsRowVersion();
        });

        modelBuilder.Entity<FnbRecipeLine>(e =>
        {
            e.ToTable("fnb_recipe_line");
            e.HasKey(x => x.id);
            e.Property(x => x.quantity).HasPrecision(18, 6);
            e.Property(x => x.sort).HasDefaultValue(0);
            e.Property(x => x.remark).HasColumnType("varchar(600)");
        });

        modelBuilder.Entity<FnbOrder>(e =>
        {
            e.ToTable("fnb_order");
            e.HasKey(x => x.id);
            e.Property(x => x.source_type).HasColumnType("varchar(20)");
            e.Property(x => x.external_order_no).HasColumnType("varchar(256)");
            e.Property(x => x.display_no).HasColumnType("varchar(128)");
            e.Property(x => x.business_date).HasColumnType("date");
            e.Property(x => x.table_no).HasColumnType("varchar(100)");
            e.Property(x => x.order_status).HasColumnType("varchar(24)");
            e.Property(x => x.platform_status).HasColumnType("varchar(100)");
            e.Property(x => x.refund_status).HasColumnType("varchar(20)");
            e.Property(x => x.refund_status).HasDefaultValue("none");
            e.Property(x => x.total_amount).HasPrecision(19, 4);
            e.Property(x => x.refund_amount).HasPrecision(19, 4);
            e.Property(x => x.review_status).HasColumnType("varchar(20)");
            e.Property(x => x.review_status).HasDefaultValue("pending");
            e.Property(x => x.remark).HasColumnType("varchar(2000)");
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.row_version).IsRowVersion();
        });

        modelBuilder.Entity<FnbOrderLine>(e =>
        {
            e.ToTable("fnb_order_line");
            e.HasKey(x => x.id);
            e.Property(x => x.line_key).HasColumnType("varchar(256)");
            e.Property(x => x.option_key).HasColumnType("varchar(256)");
            e.Property(x => x.option_key).HasDefaultValue("");
            e.Property(x => x.item_name).HasColumnType("varchar(300)");
            e.Property(x => x.spec_name).HasColumnType("varchar(200)");
            e.Property(x => x.options_text).HasColumnType("varchar(2000)");
            e.Property(x => x.quantity).HasPrecision(18, 6);
            e.Property(x => x.cancelled_qty).HasPrecision(18, 6);
            e.Property(x => x.cancelled_qty).HasDefaultValue(0m);
            e.Property(x => x.is_inventory_line).HasDefaultValue(true);
            e.Property(x => x.remark).HasColumnType("varchar(1000)");
        });

        modelBuilder.Entity<FnbOrderImport>(e =>
        {
            e.ToTable("fnb_order_import");
            e.HasKey(x => x.id);
            e.Property(x => x.source_method).HasColumnType("varchar(20)");
            e.Property(x => x.dedupe_key).HasColumnType("varchar(256)");
            e.Property(x => x.process_status).HasColumnType("varchar(24)");
            e.Property(x => x.captured_at).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<FnbStockDocument>(e =>
        {
            e.ToTable("fnb_stock_document");
            e.HasKey(x => x.id);
            e.Property(x => x.document_no).HasColumnType("varchar(80)");
            e.Property(x => x.document_type).HasColumnType("varchar(32)");
            e.Property(x => x.status).HasColumnType("varchar(20)");
            e.Property(x => x.status).HasDefaultValue("draft");
            e.Property(x => x.source_client).HasColumnType("varchar(20)");
            e.Property(x => x.business_date).HasColumnType("date");
            e.Property(x => x.occurred_at).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.reason_code).HasColumnType("varchar(32)");
            e.Property(x => x.reference_no).HasColumnType("varchar(200)");
            e.Property(x => x.remark).HasColumnType("varchar(2000)");
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.row_version).IsRowVersion();
        });

        modelBuilder.Entity<FnbStockDocumentLine>(e =>
        {
            e.ToTable("fnb_stock_document_line");
            e.HasKey(x => x.id);
            e.Property(x => x.item_name).HasColumnType("varchar(200)");
            e.Property(x => x.input_qty).HasPrecision(18, 6);
            e.Property(x => x.input_unit_name).HasColumnType("varchar(40)");
            e.Property(x => x.input_to_base).HasPrecision(18, 6);
            e.Property(x => x.planned_qty).ValueGeneratedOnAddOrUpdate();
            e.Property(x => x.actual_qty).HasPrecision(18, 6);
            e.Property(x => x.actual_qty).HasDefaultValue(0m);
            e.Property(x => x.shortage_qty).ValueGeneratedOnAddOrUpdate();
            e.Property(x => x.input_unit_price).HasPrecision(19, 6);
            e.Property(x => x.actual_amount).HasPrecision(19, 6);
            e.Property(x => x.actual_amount).HasDefaultValue(0m);
            e.Property(x => x.remark).HasColumnType("varchar(1000)");
        });

        modelBuilder.Entity<FnbStockMovement>(e =>
        {
            e.ToTable("fnb_stock_movement");
            e.HasKey(x => x.id);
            e.Property(x => x.quantity).HasPrecision(18, 6);
            e.Property(x => x.amount).HasPrecision(19, 6);
            e.Property(x => x.delta_qty).ValueGeneratedOnAddOrUpdate();
            e.Property(x => x.delta_amount).ValueGeneratedOnAddOrUpdate();
            e.Property(x => x.balance_qty).HasPrecision(18, 6);
            e.Property(x => x.balance_amount).HasPrecision(19, 6);
            e.Property(x => x.created_at).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<FnbStocktakeLine>(e =>
        {
            e.ToTable("fnb_stocktake_line");
            e.HasKey(x => x.id);
            e.Property(x => x.system_qty).HasPrecision(18, 6);
            e.Property(x => x.counted_qty).HasPrecision(18, 6);
            e.Property(x => x.difference_qty).ValueGeneratedOnAddOrUpdate();
            e.Property(x => x.remark).HasColumnType("varchar(1000)");
            e.Property(x => x.row_version).IsRowVersion();
        });

    }
}
