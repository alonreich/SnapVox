using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using log4net;

namespace snapvox.foundation.core
{
    public static class StringExtensions
    {
        private static readonly ILog LOG = snapvox.foundation.core.LogHelper.GetLogger(typeof(StringExtensions));
        private const string RGBIV = "dlgjowejgogkklwj";
        private const string KEY = "lsjvkwhvwujkagfauguwcsjgu2wueuff";

        public static string FormatWith(this string format, object source)
        {
            return FormatWith(format, null, source);
        }

        public static string FormatWith(this string format, IFormatProvider provider, object source)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            IDictionary<string, object> properties = new Dictionary<string, object>();
            foreach (var propertyInfo in source.GetType().GetProperties())
            {
                if (propertyInfo.CanRead && propertyInfo.CanWrite)
                {
                    object value = propertyInfo.GetValue(source, null);
                    if (propertyInfo.PropertyType != typeof(IDictionary<string, string>))
                    {
                        properties.Add(propertyInfo.Name, value);
                    }
                    else
                    {
                        IDictionary<string, string> dictionary = (IDictionary<string, string>) value;
                        foreach (var propertyKey in dictionary.Keys)
                        {
                            properties.Add(propertyKey, dictionary[propertyKey]);
                        }
                    }
                }
            }

            Regex r = new Regex(@"(?<start>\x7b)+(?<property>[\w\.\[\]]+)(?<format>:[^\x7d]+)?(?<end>\x7d)+",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            List<object> values = new List<object>();
            string rewrittenFormat = r.Replace(format, delegate(Match m)
            {
                Group startGroup = m.Groups["start"];
                Group propertyGroup = m.Groups["property"];
                Group formatGroup = m.Groups["format"];
                Group endGroup = m.Groups["end"];

                values.Add(properties.TryGetValue(propertyGroup.Value, out var value) ? value : source);
                return new string('{', startGroup.Captures.Count) + (values.Count - 1) + formatGroup.Value + new string('}', endGroup.Captures.Count);
            });

            return string.Format(provider, rewrittenFormat, values.ToArray());
        }

        public static string Encrypt(this string clearText)
        {
            string returnValue = clearText;
            try
            {
                byte[] clearTextBytes = Encoding.ASCII.GetBytes(clearText);
                using Aes rijn = Aes.Create();

                using MemoryStream ms = new MemoryStream();
                byte[] rgbIV = Encoding.ASCII.GetBytes(RGBIV);
                byte[] key = Encoding.ASCII.GetBytes(KEY);
                using CryptoStream cs = new CryptoStream(ms, rijn.CreateEncryptor(key, rgbIV), CryptoStreamMode.Write);
                cs.Write(clearTextBytes, 0, clearTextBytes.Length);
                cs.FlushFinalBlock();

                returnValue = Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                LOG.ErrorFormat("Error encrypting, error: {0}", ex.Message);
            }

            return returnValue;
        }

        public static string Decrypt(this string encryptedText)
        {
            string returnValue = encryptedText;
            try
            {
                byte[] encryptedTextBytes = Convert.FromBase64String(encryptedText);
                using MemoryStream ms = new MemoryStream();
                using Aes rijn = Aes.Create();

                byte[] rgbIV = Encoding.ASCII.GetBytes(RGBIV);
                byte[] key = Encoding.ASCII.GetBytes(KEY);

                using CryptoStream cs = new CryptoStream(ms, rijn.CreateDecryptor(key, rgbIV), CryptoStreamMode.Write);
                cs.Write(encryptedTextBytes, 0, encryptedTextBytes.Length);
                cs.FlushFinalBlock();
                returnValue = Encoding.ASCII.GetString(ms.ToArray());
            }
            catch (Exception ex)
            {
                LOG.ErrorFormat("Error decrypting {0}, error: {1}", encryptedText, ex.Message);
            }

            return returnValue;
        }
    }
}
