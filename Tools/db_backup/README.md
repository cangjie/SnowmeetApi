# snowmeet 数据库备份

服务器上的 SQL Server 是 **Express 版**：没有 SQL Agent、不能压缩备份，所以用 cron 调用这里的 Python 脚本。
脚本做三件事：`BACKUP DATABASE` → gzip → 传到 S3 私有前缀，每一步都有校验，并记录状态、做健康检查。

## 备份计划（snowmeet_new）

| 备份 | 时间（服务器本地时间 = 北京时间） | S3 目录 | S3 保留 |
|---|---|---|---|
| 完整 | 每天 02:30 | `private/backup/db/snowmeet_new/full/` | 7 天 |
| 差异 | 09:00～17:00 每半小时（17 次）+ 00:10 收尾（1 次） | `private/backup/db/snowmeet_new/diff/` | 6 天（见下） |
| `legacy`、`snowmeet` | 每月 1 号完整备份 | `private/backup/db/<库名>/full/` | 约 60 天（留最近 2 份） |
| 手动完整备份（`full --copy-only`） | 随时 | `private/backup/db/<库名>/adhoc/` | 不自动过期 |

- 一份完整备份和它的 18 份差异备份是一组：当天 09:00～17:00 的差异备份，加次日 00:10 的那份，都以当天 02:30 的完整备份为基准。
- 还原时只需要：同一个基准的完整备份 + 最近的一份差异备份。差异备份文件名里的 `base_<时间>` 就是它的基准。
- 最长丢失时间：营业时间约 30 分钟；17:00～00:10 之间没有备份（晚间批处理在 00:10 被保住）；库是 SIMPLE 恢复模式，恢复不到某一分钟。
- 需要 FULL 恢复模式 + 日志备份才能做到 15 分钟级，这里没有做。

## 为什么差异备份目录是 6 天、完整备份目录是 7 天

用户要求「完整备份过期时，它的差异备份跟着一起删」。S3 生命周期按「创建时间 + N 天，向后取整到下一个 UTC 零点」过期。
北京时间 02:30 的完整备份属于前一个 UTC 日，09:00 以后的差异备份属于当天 UTC 日，所以差异目录比完整目录少 1 天，两者在同一时刻（第 8 天北京时间 08:00）一起过期。
`python3 -m snowmeet_backup plan` 会打印这个对齐检查，`tests/test_core.py` 里有对应测试。
**前提：完整备份必须在北京时间 08:00 前上传完**（脚本超过会告警）。

## 目录

- `snowmeet_backup/core.py`：命名、过期对齐、状态文件、健康检查（纯逻辑，无外部依赖）
- `snowmeet_backup/jobs.py`：`full` / `diff` / `check` / `drill` 四个任务
- `snowmeet_backup/real.py`：真实的 SQL Server（pymssql）、S3（aws 命令行）、gzip、告警
- `cron.example`、`s3-lifecycle.example.json`：定时任务和 S3 过期规则的示例（**示例，没有安装**）
- `tests/`：单元测试，`python3 -m unittest discover -s tests -t .`（在本目录下运行）

## 命令

```bash
python3 -m snowmeet_backup plan                 # 打印计划和过期对齐检查，不连接任何东西
python3 -m snowmeet_backup full                 # 完整备份（改变差异备份的基准）
python3 -m snowmeet_backup full --copy-only     # 手动备份：不改变基准，传到 adhoc/
python3 -m snowmeet_backup full --db legacy     # 其他库的完整备份（传完就删本机文件）
python3 -m snowmeet_backup diff                 # 差异备份
python3 -m snowmeet_backup check                # 健康检查，有问题就告警
python3 -m snowmeet_backup drill --yes          # 还原演练：还原成临时库 snowmeet_new_rt，DBCC CHECKDB，再删掉
```

必需的环境变量：`SNOWMEET_BACKUP_BUCKET`（S3 桶名，不写进代码，因为这个仓库是公开的）。可选：
`SNOWMEET_SQL_CONFIG`（默认 `/home/ubuntu/webs/SnowmeetApi/config.sqlServer`）、`SNOWMEET_BACKUP_STAGING`（默认 `/var/opt/mssql/backup`）、
`SNOWMEET_BACKUP_STATE_DIR`（默认 `/var/lib/snowmeet-backup`）、`SNOWMEET_BACKUP_PYDEPS`。

脚本以 **root** 运行（要写 mssql 用户的目录、读 `config.sqlServer`）；S3 用服务器的实例角色，没有密钥。
`config.sqlServer` 只在进程内读取，不打印、不写日志。

## 上线步骤（每一步动服务器之前都要先和用户说明）

1. 代码走 GitHub：本地提交 → 推送 → 服务器 `git pull`（部署铁律，不要 scp）。这个目录不是 .NET 项目的一部分，不影响编译。
2. pymssql：现在靠 `/home/ubuntu/fnb-v4-deploy-20261006/deps`（**那个目录不能删**）。更稳的做法是装到系统里：`sudo pip3 install pymssql`。
3. 在 AWS 控制台设 S3 生命周期规则（见 `s3-lifecycle.example.json`）。
   **只对 `full/`、`diff/` 这几个前缀设规则**，不要设在整个 `private/backup/` 上，否则会误伤已有的老备份和回滚归档。
   不要直接用示例 JSON 调 `put-bucket-lifecycle-configuration`：它会替换整个桶的规则，桶上可能已经有别的规则（服务器角色看不到）。
4. 手动跑第一次 `full`（建立基准），再手动跑一次 `diff`、`check`，在 S3 里确认文件和 `.sha256` 都在。
5. 低峰时手动跑一次 `drill --yes`，确认还原演练能跑通。
6. 安装 cron：把 `cron.example` 复制成 `/etc/cron.d/snowmeet-backup`（属主 root，权限 644），填上桶名。
7. 可选：`/etc/logrotate.d/snowmeet-backup`：`/var/log/snowmeet-backup.log { weekly rotate 8 compress missingok notifempty }`。

## 还原（灾难恢复）

先还原成别的库名验证，确认没问题再决定是否替换线上库。

```bash
# 1) 从 S3 下载同一组的完整备份和最近一份差异备份（差异文件名里 base_ 后面就是它的基准完整备份）
aws s3 cp s3://<桶名>/private/backup/db/snowmeet_new/full/<完整备份>.bak.gz . --region cn-northwest-1
aws s3 cp s3://<桶名>/private/backup/db/snowmeet_new/diff/<差异备份>.bak.gz . --region cn-northwest-1
aws s3 cp s3://<桶名>/private/backup/db/snowmeet_new/full/<完整备份>.bak.gz.sha256 . --region cn-northwest-1
sha256sum -c <完整备份>.bak.gz.sha256 && gunzip *.gz
```

```sql
RESTORE FILELISTONLY FROM DISK = N'/path/完整备份.bak';        -- 看逻辑文件名
RESTORE DATABASE [snowmeet_new_restore] FROM DISK = N'/path/完整备份.bak'
  WITH MOVE N'<数据文件逻辑名>' TO N'/var/opt/mssql/data/snowmeet_new_restore.mdf',
       MOVE N'<日志文件逻辑名>' TO N'/var/opt/mssql/data/snowmeet_new_restore_log.ldf', NORECOVERY;
RESTORE DATABASE [snowmeet_new_restore] FROM DISK = N'/path/差异备份.bak' WITH RECOVERY;
DBCC CHECKDB ([snowmeet_new_restore]) WITH NO_INFOMSGS;
```

## 运维规矩

- 手动做完整备份**必须带 `COPY_ONLY`**（用 `full --copy-only`）。否则差异备份的基准会被换掉，之后的差异备份就找不到对应的完整备份。
  脚本会对比 msdb，发现基准被换就停止差异备份并告警，需要重新跑一次 `full`。
- 04:30 服务器每天重启，02:30 的完整备份必须在 04:15 前做完（脚本超过会告警）。
- 备份文件和 S3 里的 `.sha256` 一起保存；S3 上的服务器角色删不了东西，过期靠生命周期规则。
- 备份里有顾客手机号等个人信息：只放私有前缀，上传时带服务端加密（`--sse AES256`）。
- 磁盘剩余低于 5 GB 会告警，备份前也会检查剩余空间是否够用。

## 告警

目前脚本把问题写到日志和 syslog（`journalctl -t snowmeet-backup`）。如果存在可执行的 `/etc/snowmeet-backup/alert.sh`，
脚本会以 `alert.sh <级别> <内容>` 调用它（用来接企业微信等）。后端里已有企业微信文字提醒（`FnbWeComController.SendText`），
但要从脚本推到企业微信，需要后端加一个带令牌的小接口，模式参照 `MeituanCollectorMonitor`，这一步还没做。

## 已知限制

- 单元测试用假的 SQL 和 S3 覆盖流程。2026-11-15 在真实服务器上手动试跑过 `full`（76 秒，946MB → gzip 302MB）、`diff`（4 秒）、
  `check` 和 `drill`（40 秒，`DBCC CHECKDB` 0 个错误），S3 上的文件逐个核对过大小和 sha256。**cron 还没有安装**，S3 过期规则也要在控制台另设。
- 还原演练要占用等于源库数据文件原始大小的磁盘（现在约 3.7 GB），不是备份文件的大小；脚本按这个检查剩余空间。
- Express 不能压缩，完整备份约等于库的已用大小（`web_api_log` 清理后约 1 GB），gzip 在传 S3 之前做。
- 差异备份是累计的，到 17:00 最大。库明显变大（完整备份超过约 3 GB）时，应改成「每周日完整 + 每天差异」。
