using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System.Diagnostics;
using System.Net;
using snapvox.foundation.core;
using log4net;

namespace snapvox.foundation.Help
{

    public static class HelpFileLoader
    {
        private static readonly ILog Log = snapvox.foundation.core.LogHelper.GetLogger(typeof(HelpFileLoader));

        private const string ExtHelpUrl = "";

        public static void LoadHelp()
        {
            string uri = FindOnlineHelpUrl(Language.CurrentLanguage) ?? Language.HelpFilePath;
            Process.Start(uri);
        }

        private static string FindOnlineHelpUrl(string currentIETF)
        {
            string ret = null;

            string extHelpUrlForCurrrentIETF = ExtHelpUrl;

            if (!currentIETF.Equals("en-US"))
            {
                extHelpUrlForCurrrentIETF += currentIETF.ToLower() + "/";
            }

            HttpStatusCode? httpStatusCode = GetHttpStatus(extHelpUrlForCurrrentIETF);
            if (httpStatusCode == HttpStatusCode.OK)
            {
                ret = extHelpUrlForCurrrentIETF;
            }
            else if (httpStatusCode != null && !extHelpUrlForCurrrentIETF.Equals(ExtHelpUrl))
            {
                Log.DebugFormat("Localized online help not found at {0}, will try {1} as fallback", extHelpUrlForCurrrentIETF, ExtHelpUrl);
                httpStatusCode = GetHttpStatus(ExtHelpUrl);
                if (httpStatusCode == HttpStatusCode.OK)
                {
                    ret = ExtHelpUrl;
                }
                else
                {
                    Log.WarnFormat("{0} returned status {1}", ExtHelpUrl, httpStatusCode);
                }
            }
            else if (httpStatusCode == null)
            {
                Log.Info("Internet connection does not seem to be available, will load help from file System.");
            }

            return ret;
        }

        private static HttpStatusCode? GetHttpStatus(string url)
        {
            return null;
        }
    }
}
