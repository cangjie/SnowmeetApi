using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipelines;
using System.Net;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MediaHelper : ControllerBase
    {
        public MediaHelper()
        {

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
    }


}

