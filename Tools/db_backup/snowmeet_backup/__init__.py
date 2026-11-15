"""snowmeet 数据库备份：完整备份 + 差异备份 → gzip → S3（私有前缀），带校验、状态和健康检查。

不依赖 SQL Server Agent（Express 版没有），由 cron 调用。说明见 ../README.md。
"""
