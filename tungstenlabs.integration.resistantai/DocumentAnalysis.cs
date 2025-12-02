using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Color = System.Drawing.Color;
using System.Net.Http;
using System.Net.Http.Headers;

namespace tungstenlabs.integration.resistantai
{
    public class ContentHidingEntry
    {
        public int PageId { get; set; }
        public string NewText { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class DocumentAnalysis
    {
        
        public const string RAI_PROXY_ENABLE = "RAI-PROXY-ENABLE";
        public const string RAI_PROXY_URL = "RAI-PROXY-URL";
        public const string RAI_PROXY_USERNAME = "RAI-PROXY-USERNAME";
        public const string RAI_PROXY_PASSWORD = "RAI-PROXY-PASSWORD";

        
        public const string RAI_CLIENT_TOKEN = "RAI-CLIENT-TOKEN";

        private IWebProxy _cachedProxy = null;
        private DO_AuthCodeParamteres AuthToken { get; set; }

        /// <summary>
        /// Entry point – gets RAI token from TA, aligns proxy with APIHelper, fetches metadata,
        /// draws bounding boxes on the TIFF from KTA, and creates a new KTA document.
        /// Returns the new KTA DocumentId.
        /// </summary>
        public string GetDocumentWithBoundingBoxes(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret, string DocID, string TASDKURL, string TASession, string SubmissionID, string Category)
        {
            
            _cachedProxy = GetProxyIfEnabled(TASession, TASDKURL);

            
            AuthToken = GetTokenFromTA(TASession, TASDKURL);

            if (AuthToken == null || string.IsNullOrEmpty(AuthToken.access_token))
                throw new Exception("Auth Token is empty!");

            
            return FetchResultsWithMetadataAsync(AuthenticationURL, SubmissionURL, DocID, TASDKURL, TASession, SubmissionID, Category, ClientID, ClientSecret ).GetAwaiter().GetResult();
        }

        #region Token + Proxy handling (aligned with APIHelper.cs)

        private DO_AuthCodeParamteres RefreshAuthToken(string URL, string ClientID, string ClientSecret)
        {
            // Prepare Basic Auth header
            string credentials = string.Format("{0}:{1}", ClientID, ClientSecret);
            string base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));

            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(URL);
            httpWebRequest.ContentType = "application/x-www-form-urlencoded";
            httpWebRequest.Accept = "application/json";
            httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "Basic " + base64Credentials);
            httpWebRequest.Method = "POST";

            if (_cachedProxy != null)
            {
                httpWebRequest.Proxy = _cachedProxy;
            }

            string requestBody = "grant_type=client_credentials&scope=submissions.read submissions.write";
            byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
            httpWebRequest.ContentLength = requestBodyBytes.Length;

            using (var requestStream = httpWebRequest.GetRequestStream())
            {
                requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                requestStream.Flush();
            }

            string responseText = string.Empty;
            DO_AuthCodeParamteres bsObj2;

            HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            var encoding = ASCIIEncoding.UTF8;

            using (var reader = new StreamReader(httpWebResponse.GetResponseStream(), encoding))
            {
                responseText = reader.ReadToEnd();
            }

            using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(responseText)))
            {
                DataContractJsonSerializer deserializer = new DataContractJsonSerializer(typeof(DO_AuthCodeParamteres));
                bsObj2 = (DO_AuthCodeParamteres)deserializer.ReadObject(ms);
            }

            return bsObj2;
        }

        private DO_AuthCodeParamteres GetTokenFromTA(string taSessionId, string taSdkUrl)
        {
            List<string> vars = new List<string>() { RAI_CLIENT_TOKEN };
            ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
            var sv = serverVariableHelper.GetServerVariables(taSessionId, taSdkUrl, vars);

            return new DO_AuthCodeParamteres()
            {
                access_token = sv[RAI_CLIENT_TOKEN].Value
            };
        }

        private IWebProxy GetProxyIfEnabled(string taSessionId, string taSdkUrl)
        {
            try
            {
                ServerVariableHelper helper = new ServerVariableHelper();

                List<string> vars = new List<string>()
                {
                    RAI_PROXY_ENABLE,
                    RAI_PROXY_URL,
                    RAI_PROXY_USERNAME,
                    RAI_PROXY_PASSWORD
                };

                var sv = helper.GetServerVariables(taSessionId, taSdkUrl, vars);

                bool proxyEnabled = sv[RAI_PROXY_ENABLE].Value.ToLower() == "true";
                string proxyUrl = sv[RAI_PROXY_URL].Value;
                string proxyUser = sv[RAI_PROXY_USERNAME].Value;
                string proxyPass = sv[RAI_PROXY_PASSWORD].Value;

                if (!proxyEnabled || string.IsNullOrWhiteSpace(proxyUrl))
                    return null;

                WebProxy proxy = new WebProxy(proxyUrl)
                {
                    BypassProxyOnLocal = false,
                    BypassList = Array.Empty<string>()
                };

                if (!string.IsNullOrWhiteSpace(proxyUser))
                {
                    proxy.Credentials = new NetworkCredential(proxyUser, proxyPass);
                }
                else
                {
                    proxy.UseDefaultCredentials = true;
                }

                return proxy;
            }
            catch
            {
                return null;
            }
        }

        private bool IsProxyAuthError(HttpRequestException ex)
        {
            var webEx = ex.InnerException as WebException;
            if (webEx == null)
                return false;

            var response = webEx.Response as HttpWebResponse;
            if (response == null)
                return false;

            return response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired; // 407
        }

        #endregion

        #region Fetch RAI results + draw bounding boxes

        /// <summary>
        /// Old synchronous version kept for compatibility (still uses WebRequest).
        /// Not used by GetDocumentWithBoundingBoxes, which uses the async version.
        /// </summary>
        private string FetchResultsWithMetadata(string SubmissionURL, string DocID, string TASDKURL, string TASession, string SubmissionID, string Category)
        {
            if (AuthToken == null || AuthToken.access_token == "")
            {
                throw new Exception("Auth Token is empty!");
            }

            HttpWebRequest httpWebRequest;
            HttpWebResponse httpWebResponse;
            string text = "";
            string requrl = $"{SubmissionURL}/{SubmissionID}/fraud?with_metadata=true";
            int counter = 0;
            int max = 15;
            int delay = 4000;

            while (counter <= max && string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    Thread.Sleep(delay);
                    httpWebRequest = (HttpWebRequest)WebRequest.Create(requrl);
                    httpWebRequest.Proxy = _cachedProxy ?? GetProxyIfEnabled(TASession, TASDKURL);

                    httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, $"Bearer {AuthToken.access_token}");
                    httpWebRequest.Method = "GET";
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Accept = "*/*";

                    using (httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
                    using (var sr = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.UTF8))
                    {
                        text = sr.ReadToEnd();
                    }
                }
                catch (WebException ex)
                {
                    if (counter >= max)
                    {
                        throw new Exception($"Too many WebExceptions after {counter} retries - {ex.Message}", ex);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"An unexpected error occurred: {ex.Message}", ex);
                }

                delay += (1000 * counter);
                counter++;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception("Could not get the results from ResistantAI API.");
            }

            List<ContentHidingEntry> CoordinatesList = ExtractContentHidingEntries(text, Category);

            return DrawBoundingBoxesOnImage(DocID, CoordinatesList, TASDKURL, TASession);
        }

        /// <summary>
        /// Async version aligned with APIHelper FetchResultsAsync:
        /// - Uses HttpClient + HttpClientHandler
        /// - Uses _cachedProxy
        /// - Retries with backoff
        /// - Handles proxy 407 and token refresh (401/403) via RefreshAuthToken + TA update
        /// Returns new KTA DocumentId.
        /// </summary>
        private async Task<string> FetchResultsWithMetadataAsync(string AuthenticationURL, string SubmissionURL, string DocID, string TASDKURL, string TASession, string SubmissionID, string Category, string ClientID, string ClientSecret)
        {
            if (AuthToken == null || string.IsNullOrWhiteSpace(AuthToken.access_token))
            {
                throw new Exception("Auth Token is empty!");
            }

            HttpClientHandler handler = new HttpClientHandler();

            if (_cachedProxy != null)
            {
                handler.Proxy = _cachedProxy;
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }

            using (HttpClient httpClient = new HttpClient(handler))
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", AuthToken.access_token);

                string requrl = $"{SubmissionURL}/{SubmissionID}/fraud?with_metadata=true";
                string text = "";
                int counter = 0;
                int max = 15;
                int delay = 4000;

                while (counter <= max && string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        await Task.Delay(delay).ConfigureAwait(false);

                        HttpResponseMessage response = await httpClient.GetAsync(requrl).ConfigureAwait(false);

                        // Handle Unauthorized / Forbidden -> refresh token, update TA, retry
                        if (response.StatusCode == HttpStatusCode.Unauthorized ||
                            response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            // Refresh token from RAI
                            AuthToken = RefreshAuthToken(AuthenticationURL, ClientID, ClientSecret);

                            // Update TA server variable RAI-CLIENT-TOKEN
                            List<string> vars = new List<string>() { RAI_CLIENT_TOKEN };
                            ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                            var dict = serverVariableHelper.GetServerVariables(TASession, TASDKURL, vars);
                            dict[RAI_CLIENT_TOKEN] = new KeyValuePair<string, string>(
                                dict[RAI_CLIENT_TOKEN].Key,
                                AuthToken.access_token
                            );

                            Dictionary<string, string> newDict =
                                dict.ToDictionary(kvp => kvp.Value.Key, kvp => kvp.Value.Value);

                            serverVariableHelper.UpdateServerVariables(newDict, TASession, TASDKURL);

                            // Update HttpClient header
                            httpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", AuthToken.access_token);

                            // Move to next attempt
                            counter++;
                            delay += (1000 * counter);
                            continue;
                        }

                        response.EnsureSuccessStatusCode();

                        text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (IsProxyAuthError(ex))
                        {
                            throw new Exception(
                                "Proxy authentication failed (HTTP 407). Please verify RAI-PROXY-USERNAME and RAI-PROXY-PASSWORD settings in TA Server Variables.",
                                ex
                            );
                        }

                        if (counter >= max)
                        {
                            throw new Exception(
                                $"Too many HttpRequestExceptions after {counter} retries - {ex.Message}",
                                ex
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"An unexpected error occurred: {ex.Message}", ex);
                    }

                    delay += (1000 * counter);
                    counter++;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new Exception("Could not get the results from ResistantAI API.");
                }

                List<ContentHidingEntry> CoordinatesList = ExtractContentHidingEntries(text, Category);

                // Create new KTA document with bounding boxes and return DocumentId
                return await DrawBoundingBoxesOnImageAsync(DocID, CoordinatesList, TASDKURL, TASession)
                    .ConfigureAwait(false);
            }
        }

        public List<ContentHidingEntry> ExtractContentHidingEntries(string jsonData, string category)
        {
            var contentHidingEntries = new List<ContentHidingEntry>();

            try
            {
                JObject data = JObject.Parse(jsonData);
                if (data.TryGetValue("indicators", out JToken indicatorsToken) && indicatorsToken is JArray indicators)
                {
                    foreach (var indicator in indicators)
                    {
                        if ((string)indicator["category"] == category &&
                            indicator["metadata"]?["data"] is JArray metadataArray)
                        {
                            foreach (var entry in metadataArray)
                            {
                                int pageId = entry["page_id"]?.ToObject<int>() ?? 0;
                                string newText = entry["new_text"]?.ToString() ?? "";

                                // Case 1: bbox is a single object
                                if (entry["bbox"] is JObject bbox)
                                {
                                    contentHidingEntries.Add(new ContentHidingEntry
                                    {
                                        PageId = pageId,
                                        NewText = newText,
                                        X = bbox["x"]?.ToObject<float>() ?? 0f,
                                        Y = bbox["y"]?.ToObject<float>() ?? 0f,
                                        Width = bbox["width"]?.ToObject<float>() ?? 0f,
                                        Height = bbox["height"]?.ToObject<float>() ?? 0f
                                    });
                                }
                                // Case 2: bbox is an array of objects
                                else if (entry["bbox"] is JArray bboxArray)
                                {
                                    foreach (var bboxEntry in bboxArray)
                                    {
                                        if (bboxEntry is JObject bboxObj)
                                        {
                                            contentHidingEntries.Add(new ContentHidingEntry
                                            {
                                                PageId = pageId,
                                                NewText = newText,
                                                X = bboxObj["x"]?.ToObject<float>() ?? 0f,
                                                Y = bboxObj["y"]?.ToObject<float>() ?? 0f,
                                                Width = bboxObj["width"]?.ToObject<float>() ?? 0f,
                                                Height = bboxObj["height"]?.ToObject<float>() ?? 0f
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (JsonReaderException ex)
            {
                
            }
            catch (Exception ex)
            {
                
            }

            return contentHidingEntries;
        }

        public string DrawBoundingBoxesOnImage(string DocID, List<ContentHidingEntry> boundingBoxes, string TASDKURL, string TASession)
        {
            byte[] FileArray = GetKTADocumentFileAsTiff(DocID, TASDKURL, TASession);

            using (MemoryStream ms = new MemoryStream(FileArray))
            using (System.Drawing.Image image = System.Drawing.Image.FromStream(ms))
            {
                FrameDimension frameDimension = new FrameDimension(image.FrameDimensionsList[0]);
                int totalPages = image.GetFrameCount(frameDimension);

                List<Bitmap> modifiedPages = new List<Bitmap>();

                double pdfWidth = 612;  // Standard PDF width
                double pdfHeight = 792; // Standard PDF height

                for (int i = 0; i < totalPages; i++)
                {
                    image.SelectActiveFrame(frameDimension, i);

                    Bitmap pageBitmap = new Bitmap(image);
                    using (Graphics g = Graphics.FromImage(pageBitmap))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(100, Color.Yellow)))
                        {
                            foreach (var box in boundingBoxes)
                            {
                                if (box.PageId == i)
                                {
                                    float scaleX = (float)(pageBitmap.Width / pdfWidth);
                                    float scaleY = (float)(pageBitmap.Height / pdfHeight);

                                    float xImage = (float)(box.X * scaleX);
                                    float widthImage = (float)(box.Width * scaleX);
                                    float yImage = (float)((pdfHeight - box.Y - box.Height) * scaleY);
                                    float heightImage = (float)(box.Height * scaleY);

                                    g.FillRectangle(brush, xImage, yImage, widthImage, heightImage);
                                }
                            }
                        }
                    }

                    modifiedPages.Add(pageBitmap);
                }

                return CreateTADocument(modifiedPages, TASDKURL, TASession);
            }
        }

        public async Task<string> DrawBoundingBoxesOnImageAsync(string DocID, List<ContentHidingEntry> boundingBoxes, string TASDKURL, string TASession)
        {
            byte[] FileArray = await GetKTADocumentFileAsTiffAsync(DocID, TASDKURL, TASession);

            using (MemoryStream ms = new MemoryStream(FileArray))
            using (System.Drawing.Image image = System.Drawing.Image.FromStream(ms))
            {
                FrameDimension frameDimension = new FrameDimension(image.FrameDimensionsList[0]);
                int totalPages = image.GetFrameCount(frameDimension);

                List<Bitmap> modifiedPages = new List<Bitmap>();

                double pdfWidth = 612;  // Standard PDF width
                double pdfHeight = 792; // Standard PDF height

                for (int i = 0; i < totalPages; i++)
                {
                    image.SelectActiveFrame(frameDimension, i);

                    Bitmap pageBitmap = new Bitmap(image);
                    using (Graphics g = Graphics.FromImage(pageBitmap))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(100, Color.Yellow)))
                        {
                            foreach (var box in boundingBoxes)
                            {
                                if (box.PageId == i)
                                {
                                    float scaleX = (float)(pageBitmap.Width / pdfWidth);
                                    float scaleY = (float)(pageBitmap.Height / pdfHeight);

                                    float xImage = (float)(box.X * scaleX);
                                    float widthImage = (float)(box.Width * scaleX);
                                    float yImage = (float)((pdfHeight - box.Y - box.Height) * scaleY);
                                    float heightImage = (float)(box.Height * scaleY);

                                    g.FillRectangle(brush, xImage, yImage, widthImage, heightImage);
                                }
                            }
                        }
                    }

                    modifiedPages.Add(pageBitmap);
                }

                return CreateTADocument(modifiedPages, TASDKURL, TASession);
            }
        }

        #endregion

        #region 

        private byte[] GetKTADocumentFileAsTiff(string docID, string ktaSDKUrl, string sessionID)
        {
            byte[] result = new byte[1];
            byte[] buffer = new byte[4096];
            string status = "OK";

            try
            {
                var KTAGetDocumentFile = ktaSDKUrl + "/CaptureDocumentService.svc/json/GetDocumentFile2";
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);
                httpWebRequest.Proxy = null;

                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    string json = "{\"sessionId\":\"" + sessionID + "\",\"reportingData\": {\"Station\": \"\", \"MarkCompleted\": false }, \"documentId\":\"" + docID + "\", \"documentFileOptions\": { \"FileType\": \".tiff\", \"IncludeAnnotations\": 0 }}";

                    streamWriter.Write(json);
                    streamWriter.Flush();
                }

                HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                Stream receiveStream = httpWebResponse.GetResponseStream();
                Encoding encode = Encoding.GetEncoding("utf-8");
                StreamReader readStream = new StreamReader(receiveStream, encode);
                int streamContentLength = unchecked((int)httpWebResponse.ContentLength);

                using (Stream responseStream = httpWebResponse.GetResponseStream())
                {
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        int count = 0;
                        do
                        {
                            count = responseStream.Read(buffer, 0, buffer.Length);
                            memoryStream.Write(buffer, 0, count);
                        } while (count != 0);

                        result = memoryStream.ToArray();
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                status = "An error occured: " + ex.ToString();
                return result;
            }
        }

        private async Task<byte[]> GetKTADocumentFileAsTiffAsync(string docID, string ktaSDKUrl, string sessionID)
        {
            byte[] result = Array.Empty<byte>();
            string requestUrl = $"{ktaSDKUrl}/CaptureDocumentService.svc/json/GetDocumentFile2";
            int maxAttempts = 6;
            int delayMs = 5000;
            int attempt = 0;

            while (attempt < maxAttempts)
            {
                try
                {
                    var handler = new HttpClientHandler
                    {
                        Proxy = null,
                        UseProxy = false
                    };

                    using (HttpClient httpClient = new HttpClient(handler))
                    {
                        var requestPayload = new
                        {
                            sessionId = sessionID,
                            reportingData = new { Station = "", MarkCompleted = false },
                            documentId = docID,
                            documentFileOptions = new { FileType = ".tiff", IncludeAnnotations = 0 }
                        };

                        string json = JsonConvert.SerializeObject(requestPayload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await httpClient.PostAsync(requestUrl, content).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();

                        result = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                        if (result.Length > 0)
                        {
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (attempt >= maxAttempts - 1)
                    {
                        throw new Exception($"Failed to get KTA document after {maxAttempts} attempts. Last error: {ex.Message}", ex);
                    }
                    // else swallow and retry
                }

                await Task.Delay(delayMs).ConfigureAwait(false);
                attempt++;
            }

            throw new Exception("Could not retrieve the KTA document as TIFF after retries.");
        }

        private string CreateTADocument(List<Bitmap> pages, string ktaSDKUrl, string sessionID)
        {
            JObject result = null;
            string returnDocumentId = string.Empty;

            try
            {
                var createDocumentURL = ktaSDKUrl + "/CaptureDocumentService.svc/json/CreateDocument3";
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(createDocumentURL);
                request.Proxy = null;

                request.Timeout = 300000;
                request.ContentType = "application/json";
                request.Method = "POST";

                var pageDataList = pages.Select(page => new
                {
                    Base64Data = Convert.ToBase64String(BitmapToByteArray(page, ImageFormat.Tiff)),
                    MimeType = "image/tiff",
                    Data = (object)null,
                    RuntimeFields = (object)null
                }).ToArray();

                var requestPayload = new
                {
                    sessionId = sessionID,
                    reportingData = (object)null,
                    parentId = "",
                    runtimeFolderFields = (object)null,
                    folderTypeId = "",
                    documentDataInput = new
                    {
                        PageDataList = pageDataList,
                        MimeType = "image/tiff"
                    },
                    insertIndex = 0
                };

                string json = JsonConvert.SerializeObject(requestPayload);

                using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                {
                    streamWriter.Write(json);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    result = JObject.Parse(reader.ReadToEnd());
                }

                if (result["d"] != null)
                {
                    JObject dObject = (JObject)result["d"];
                    returnDocumentId = dObject["DocumentId"].ToString();
                }

                return returnDocumentId;
            }
            catch (Exception ex)
            {
                throw new Exception("Error creating document", ex);
            }
        }

        private byte[] BitmapToByteArray(Bitmap bitmap, ImageFormat format)
        {
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, format);
                return memoryStream.ToArray();
            }
        }

        #endregion
    }
}
