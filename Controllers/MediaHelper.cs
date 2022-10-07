using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Collections;
//using ThoughtWorks.QRCode.Codec;
//using QRCoder;
//using System.Reflection.Emit;

using Net.Codecrete.QrCodeGenerator;


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
    }


}

