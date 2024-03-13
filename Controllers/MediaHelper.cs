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

        /*
        public MediaHelper()
        {

        }

        [HttpGet]
        public void ShowQRCode(string qrCodeText)
        {
            qrCodeText = Util.UrlDecode(qrCodeText).Trim();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://weixin.snowmeet.top/show_qrcode.aspx?qrcodetext=" + qrCodeText.Trim());
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            Stream s = res.GetResponseStream();
            Response.ContentType = "image/jpeg";



        }
        */
        /*
        [HttpGet]
        public void CreatePersonalPosterWithTextQrCode(string templatePath, int x, int y, int scale, string qrCodeText)
        {
            string realTemplatePath = Util.workingPath + templatePath;
            //Bitmap bmpTemplate = Bitmap.FromFile(realTemplatePath);
            Image imgTemplate = Bitmap.FromFile(realTemplatePath);
            QRCodeEncoder enc = new QRCodeEncoder();
            Bitmap bmpQr = enc.Encode(qrCodeText);
            Graphics g = Graphics.FromImage(imgTemplate);
            g.DrawImage(bmpQr, x, y, scale, scale);
            g.Save();
            Response.ContentType = "image/jpeg";
            PipeWriter pw = Response.BodyWriter;
            Stream s = pw.AsStream();
            imgTemplate.Save(s, ImageFormat.Jpeg);
            s.Close();
            g.Dispose();
            bmpQr.Dispose();
            imgTemplate.Dispose();
        }
        [HttpGet]
        public void DrawQrCodeTest()
        {
            string imgPath = Util.workingPath + "/images/a.jpg";
            ArrayList fileArr = new ArrayList();
            using (FileStream fs = System.IO.File.OpenRead(imgPath))
            {
                int b = fs.ReadByte();
                for (; b >= 0;)
                {
                    fileArr.Add((byte)b);
                    b = fs.ReadByte();
                }
                fs.Close();
            }
            byte[] bArr = new byte[fileArr.Count];
            for (int i = 0; i < bArr.Length; i++)
            {
                bArr[i] = (byte)fileArr[i];
            }
            Response.ContentType = "image/jpeg";
            PipeWriter pw = Response.BodyWriter;
            Stream s = pw.AsStream();
            for (int i = 0; i < bArr.Length; i++)
            {
                s.WriteByte(bArr[i]);
            }
            s.Close();

        }
        */

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
            string imgUrl = "http://weixin.snowmeet.top/" + img;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(imgUrl);
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

