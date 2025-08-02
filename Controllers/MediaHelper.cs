using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using System.Threading.Tasks;
//using ThoughtWorks.QRCode.Codec;
//using QRCoder;
//using System.Reflection.Emit;

//using Net.Codecrete.QrCodeGenerator;


namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MediaHelper : ControllerBase
    {

        
        public MediaHelper()
        {

        }
        [HttpGet("{angle}")]
        public void ShowImageRotate(string imgUrl, int angle=90)
        {
            ImageEncoder enc = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder();
            imgUrl = Util.UrlDecode(imgUrl);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(imgUrl);
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            byte[] buf = new byte[1024 * 1024 * 100];
            Stream s = res.GetResponseStream();
            Response.ContentType = "image/jpeg";
            PipeWriter pw = Response.BodyWriter;
            Stream sOut = pw.AsStream();
            Image img =  Image.Load(s);
            switch (angle)
            {
                case 180:
                    img.Mutate(x => x.RotateFlip(RotateMode.Rotate180, FlipMode.None));
                    break;
                case 270:
                    img.Mutate(x => x.RotateFlip(RotateMode.Rotate270, FlipMode.None));
                    break;
                default:
                    img.Mutate(x => x.RotateFlip(RotateMode.Rotate90, FlipMode.None));
                    break;
            }
            //img.Mutate(x => x.RotateFlip(RotateMode.Rotate90, FlipMode.None));
            img.Save(sOut, enc);
            s.Close();
            res.Close();
            req.Abort();
        }

        [HttpGet]
        public void ShowImageFromOfficialAccount(string img)
        {
            img = Util.UrlDecode(img);
            string scene = img.Split('=')[1].Trim();
            string imgUrl = "https://wxoa.snowmeet.top/api/OfficialAccountApi/GetOAQRCodeUrl?content=" + scene.Trim();
            string qrCodeUrl = Util.GetWebContent(imgUrl);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(qrCodeUrl);
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            byte[] buf = new byte[1024 * 1024 * 100];
            Stream s = res.GetResponseStream();
            int i = s.ReadByte();
            int j = 0;
            while (i >= 0)
            {
                buf[j] = (byte)i;
                i = s.ReadByte();
                j++;
            }
            s.Close();
            res.Close();
            req.Abort();
            byte[] buff = new byte[j];
            for (int k = 0; k < j; k++)
            {
                buff[k] = buf[k];
            }
            Response.ContentType = "image/jpeg";
            PipeWriter pw = Response.BodyWriter;
            Stream sOut = pw.AsStream();
            for (int k = 0; k < buff.Length; k++)
            {
                sOut.WriteByte(buff[k]);
            }
            sOut.Close();
        }

        [HttpGet]
        public void GetQRCode(string qrCodeText)
        {
            if (qrCodeText.StartsWith("http"))
            {
                qrCodeText = Util.UrlDecode(qrCodeText);
            }
            byte[] bArr = QRCoder.BitmapByteQRCodeHelper.GetQRCode(qrCodeText, QRCoder.QRCodeGenerator.ECCLevel.Q, 5);
            Response.ContentType = "image/jpeg";
            Response.ContentLength = bArr.Length;
            PipeWriter pw = Response.BodyWriter;
            Stream sOut = pw.AsStream();
            for (int k = 0; k < bArr.Length; k++)
            {
                sOut.WriteByte(bArr[k]);
            }
            sOut.Close();
        }
    }
}