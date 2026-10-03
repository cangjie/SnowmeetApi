using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace SnowmeetApi.Services.Storage
{
    // 上传文件统一存储（2026-10-03）：写 AWS 宁夏 S3 私有桶，经中国区 CloudFront（OAI）用 img.snowmeet.top 对外。
    // 库里照旧只存站内相对路径 /upload/yyyyMMdd/文件名，S3 对象键 = 去掉开头的「/」；显示域名只在 PublicBaseUrl 一处配置。
    // is_web=0 的文件放 private/ 前缀下——桶策略只给 CloudFront 开 upload/*，这类文件不对外。
    public interface IFileStorage
    {
        // 保存后返回站内相对路径（/upload/yyyyMMdd/xxx.ext），直接写进 mini_upload.file_path_name
        Task<string> SaveAsync(Stream content, string originalFileName, bool isWeb = true, string namePrefix = "");
        // 文件不存在返回 null
        Task<byte[]?> ReadAsync(string relativePath, bool isWeb = true);
        string PublicUrl(string relativePath);
    }

    public class FileStorageOptions
    {
        // S3 | Local。默认值即生产配置，服务器上不用加任何配置项；本机调试可在 appsettings 里设 FileStorage:Provider=Local
        public string Provider { get; set; } = "S3";
        public string Bucket { get; set; } = "snowmeet-uploads-673751646617-cn-northwest-1-an";
        public string Region { get; set; } = "cn-northwest-1";
        public string PublicBaseUrl { get; set; } = "https://img.snowmeet.top";

        public static FileStorageOptions From(IConfiguration config)
        {
            FileStorageOptions options = new FileStorageOptions();
            config.GetSection("FileStorage").Bind(options);
            return options;
        }
    }

    public static class UploadPaths
    {
        // 新文件名用随机串：旧的毫秒时间戳能被猜出来，图片公开后可被逐个遍历
        public static string NewRelativePath(string originalFileName, DateTime now, string namePrefix = "")
        {
            return "/upload/" + now.ToString("yyyyMMdd") + "/" + namePrefix + Guid.NewGuid().ToString("N")
                + "." + SafeExtension(originalFileName);
        }

        // 只留字母数字、转小写、最长 10 位；取不到就用 bin。原样拼用户给的后缀会把「../」之类带进对象键
        public static string SafeExtension(string fileName)
        {
            string name = fileName ?? "";
            int dot = name.LastIndexOf('.');
            string ext = dot >= 0 ? name.Substring(dot + 1) : "";
            ext = new string(ext.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
            if (ext.Length == 0 || ext.Length > 10)
            {
                return "bin";
            }
            return ext;
        }

        public static string ObjectKey(string relativePath, bool isWeb = true)
        {
            string key = (relativePath ?? "").Trim().TrimStart('/');
            if (key.Length == 0 || key.Split('/').Any(s => s == ".." || s == "."))
            {
                throw new ArgumentException("非法的文件路径：" + relativePath);
            }
            return isWeb ? key : "private/" + key;
        }

        public static string PublicUrl(string baseUrl, string relativePath)
        {
            string path = (relativePath ?? "").Trim();
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        public static string ContentType(string relativePath)
        {
            switch (SafeExtension(relativePath))
            {
                case "jpg":
                case "jpeg": return "image/jpeg";
                case "png": return "image/png";
                case "gif": return "image/gif";
                case "webp": return "image/webp";
                case "bmp": return "image/bmp";
                case "heic": return "image/heic";
                case "svg": return "image/svg+xml";
                case "mp4": return "video/mp4";
                case "mov": return "video/quicktime";
                case "mp3": return "audio/mpeg";
                case "pdf": return "application/pdf";
                case "txt": return "text/plain; charset=utf-8";
                case "xls": return "application/vnd.ms-excel";
                case "xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case "doc": return "application/msword";
                case "docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                default: return "application/octet-stream";
            }
        }

        // 迁移前的老文件还在本机磁盘：Local 存储与 S3 读不到时的回退都按这个路径找
        public static string LocalFilePath(string relativePath, bool isWeb = true)
        {
            ObjectKey(relativePath, isWeb);
            return Util.workingPath + (isWeb ? "/wwwroot" : "") + "/" + relativePath.Trim().TrimStart('/');
        }
    }

    public class LocalFileStorage : IFileStorage
    {
        private readonly FileStorageOptions _options;

        public LocalFileStorage(FileStorageOptions options)
        {
            _options = options;
        }

        public async Task<string> SaveAsync(Stream content, string originalFileName, bool isWeb = true, string namePrefix = "")
        {
            string relativePath = UploadPaths.NewRelativePath(originalFileName, DateTime.Now, namePrefix);
            string fullPath = UploadPaths.LocalFilePath(relativePath, isWeb);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (Stream s = File.Create(fullPath))
            {
                await content.CopyToAsync(s);
            }
            return relativePath;
        }

        public async Task<byte[]?> ReadAsync(string relativePath, bool isWeb = true)
        {
            string fullPath = UploadPaths.LocalFilePath(relativePath, isWeb);
            return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath) : null;
        }

        public string PublicUrl(string relativePath)
        {
            return UploadPaths.PublicUrl(_options.PublicBaseUrl, relativePath);
        }
    }

    public class S3FileStorage : IFileStorage
    {
        private readonly FileStorageOptions _options;
        private readonly IAmazonS3 _s3;

        // 凭证走默认链：mini 服务器（EC2）用挂在实例上的 IAM 角色，代码和配置里都不放密钥
        public S3FileStorage(FileStorageOptions options)
            : this(options, new AmazonS3Client(RegionEndpoint.GetBySystemName(options.Region)))
        {
        }

        public S3FileStorage(FileStorageOptions options, IAmazonS3 s3)
        {
            _options = options;
            _s3 = s3;
        }

        public async Task<string> SaveAsync(Stream content, string originalFileName, bool isWeb = true, string namePrefix = "")
        {
            string relativePath = UploadPaths.NewRelativePath(originalFileName, DateTime.Now, namePrefix);
            PutObjectRequest request = new PutObjectRequest()
            {
                BucketName = _options.Bucket,
                Key = UploadPaths.ObjectKey(relativePath, isWeb),
                InputStream = content,
                ContentType = UploadPaths.ContentType(relativePath),
                AutoCloseStream = false
            };
            // 文件名随机且不会被覆盖写，CloudFront 和客户端可以长期缓存
            request.Headers.CacheControl = "public, max-age=31536000, immutable";
            await _s3.PutObjectAsync(request);
            return relativePath;
        }

        public async Task<byte[]?> ReadAsync(string relativePath, bool isWeb = true)
        {
            try
            {
                using GetObjectResponse response = await _s3.GetObjectAsync(_options.Bucket, UploadPaths.ObjectKey(relativePath, isWeb));
                using MemoryStream ms = new MemoryStream();
                await response.ResponseStream.CopyToAsync(ms);
                return ms.ToArray();
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // 迁移过渡期：老文件可能还没同步进桶，回退读本机磁盘
                string fullPath = UploadPaths.LocalFilePath(relativePath, isWeb);
                return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath) : null;
            }
        }

        public string PublicUrl(string relativePath)
        {
            return UploadPaths.PublicUrl(_options.PublicBaseUrl, relativePath);
        }
    }

    public static class UploadRedirectExtensions
    {
        // 放在 UseStaticFiles 之后：本机磁盘上还在的老文件照旧直接返回，没有的 /upload/... 302 到图片域名
        public static IApplicationBuilder UseUploadRedirect(this IApplicationBuilder app, FileStorageOptions options)
        {
            return app.Use(async (context, next) =>
            {
                if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    && context.Request.Path.StartsWithSegments("/upload"))
                {
                    context.Response.Redirect(UploadPaths.PublicUrl(options.PublicBaseUrl,
                        context.Request.Path.Value + context.Request.QueryString.Value));
                    return;
                }
                await next(context);
            });
        }
    }

    public static class FileStorageFactory
    {
        public static IFileStorage Create(FileStorageOptions options)
        {
            if (string.Equals(options.Provider, "Local", StringComparison.OrdinalIgnoreCase))
            {
                return new LocalFileStorage(options);
            }
            return new S3FileStorage(options);
        }
    }
}
