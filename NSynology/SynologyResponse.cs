using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NSynology
{
    public class SynologyResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public Error? Error { get; set; }
        public void CheckErrorCode()
        {
            if (!Success && Error == null)
                throw new Exception("Request failed.");
            if (Error == null) return;

            var ErrorCodes = new Dictionary<int, string>()
            {
                {  100,"Unknown error." },
                {  101,"No parameter of API, method or version." },
                {  102,"The requested API does not exist." },
                {  103,"The requested method does not exist." },
                {  104,"The requested version does not support the functionality." },
                {  105,"The logged in session does not have permission." },
                {  106,"Session timeout." },
                {  107,"Session interrupted by duplicate login." },
                {  108,"Failed to upload the file." },
                {  114,"Missing required parameters." },
                {  117,"Unknown internal error." },
                {  119,"当前 DSM 会话无法用于 Synology Photos API（未安装 Photos、无相册权限，或会话无效）。" },
                {  120,"Invalid parameter." },
                {  160,"Insufficient application privilege." },

                {  400,"No such account or incorrect password." },
                {  401,"Disabled account." },
                {  402,"Denied permission."},
                {  403,"2-factor authentication code required."},
                {  404,"Failed to authenticate 2-factor authentication code."},
                {  406,"Enforce to authenticate with 2-factor authentication code."},
                {  407,"Blocked IP source."},
                {  408,"Expired password cannot change."},
                {  409,"Expired password."},
                {  410,"Password must be changed."}
            };
            if (ErrorCodes.TryGetValue(Error.Code, out string value))
            {
                throw new Exception(value);
            }
            if (Error.Errors != null && Error.Errors.Reason != null)
            {
                throw new Exception($"{Error.Errors.Name} {Error.Errors.Reason}");
            }
        }
    }
}
