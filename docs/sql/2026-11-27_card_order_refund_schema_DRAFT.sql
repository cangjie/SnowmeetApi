-- 含卡订单整单退款批次草稿。2026-11-27 按用户授权，等价 DDL 已逐条在生产事务中执行并核验。
-- 本 sqlcmd 草稿文件本身尚未作为一个整体执行验证；不要误以为生产库尚未迁移。
-- 以单独批次记录整单状态；每笔渠道退款继续沿用 payment_refund 的原退款单号。
-- sqlcmd -v COMMIT=0 默认演练回滚；1 才提交。生产执行和部署须另行授权。
SET XACT_ABORT ON;
IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51000, N'不能在系统数据库执行。', 1;
BEGIN TRY
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.punch_card', 'refund_pending') IS NULL
    ALTER TABLE dbo.punch_card ADD refund_pending bit NOT NULL
        CONSTRAINT DF_punch_card_refund_pending DEFAULT (0);

IF OBJECT_ID('dbo.card_order_refund_batch', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.card_order_refund_batch (
        id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        order_id int NOT NULL,
        status varchar(20) NOT NULL CHECK (status IN ('processing', 'succeeded', 'failed')),
        amount decimal(18,2) NOT NULL,
        create_date datetime2 NOT NULL,
        update_date datetime2 NOT NULL
    );
    CREATE INDEX IX_card_order_refund_batch_order ON dbo.card_order_refund_batch(order_id, id);
    CREATE UNIQUE INDEX UX_card_order_refund_batch_active ON dbo.card_order_refund_batch(order_id) WHERE status = 'processing';
END;

IF OBJECT_ID('dbo.card_order_refund_card', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.card_order_refund_card (
        id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id int NOT NULL,
        card_id int NOT NULL,
        CONSTRAINT UQ_card_order_refund_card UNIQUE (batch_id, card_id)
    );
END;

IF COL_LENGTH('dbo.payment_refund', 'card_refund_batch_id') IS NULL
    ALTER TABLE dbo.payment_refund ADD card_refund_batch_id int NULL;
IF COL_LENGTH('dbo.payment_refund', 'card_refund_state') IS NULL
    ALTER TABLE dbo.payment_refund ADD card_refund_state varchar(20) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.payment_refund') AND name='IX_payment_refund_card_refund_batch')
    CREATE INDEX IX_payment_refund_card_refund_batch ON dbo.payment_refund(card_refund_batch_id) WHERE card_refund_batch_id IS NOT NULL;

IF N'$(COMMIT)' = N'1' COMMIT TRANSACTION;
ELSE ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
