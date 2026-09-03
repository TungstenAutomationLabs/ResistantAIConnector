using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
/*
 * tungstenlabs.integration.resistantai.ResistantAIConnector
 *
 * End User License Agreement (EULA)
 *
 * IMPORTANT: PLEASE READ THIS AGREEMENT CAREFULLY BEFORE USING THIS SOFTWARE.
 *
 * 1. GRANT OF LICENSE: Tungsten Automation grants you a limited, non-exclusive,
 * non-transferable, and revocable license to use this software solely for the
 * purposes described in the documentation accompanying the software.
 *
 * 2. RESTRICTIONS: You may not sublicense, rent, lease, sell, distribute,
 * redistribute, assign, or otherwise transfer your rights to this software.
 * You may not reverse engineer, decompile, or disassemble this software,
 * except and only to the extent that such activity is expressly permitted by
 * applicable law notwithstanding this limitation.
 *
 * 3. COPYRIGHT: This software is protected by copyright laws and international
 * copyright treaties, as well as other intellectual property laws and treaties.
 *
 * 4. DISCLAIMER OF WARRANTY: THIS SOFTWARE IS PROVIDED "AS IS" AND ANY EXPRESS
 * OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN
 * NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
 * INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 * BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 * DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY
 * OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE
 * OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
 * OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 * 5. TERMINATION: Without prejudice to any other rights, Tungsten Automation may
 * terminate this EULA if you fail to comply with the terms and conditions of this
 * EULA. In such event, you must destroy all copies of the software and all of its
 * component parts.
 *
 * 6. GOVERNING LAW: This agreement shall be governed by the laws of USA,
 * without regard to conflicts of laws principles. Any disputes arising hereunder shall
 * be subject to the exclusive jurisdiction of the courts of USA.
 *
 * 7. ENTIRE AGREEMENT: This EULA constitutes the entire agreement between you and
 * Tungsten Automation relating to the software and supersedes all prior or contemporaneous
 * understandings regarding such subject matter. No amendment to or modification of this
 * EULA will be binding unless made in writing and signed by Tungsten Automation.
 *
 * Tungsten Automation
 * www.tungstenautomation.com
 * 03/19/2024
 */

namespace tungstenlabs.integration.resistantai
{



    // authentication dataobject
    [DataContract]
    internal class DO_AuthCodeParamteres
    {
        [DataMember] public string token_type { get; set; }
        [DataMember] public string expires_in { get; set; }
        [DataMember] public string access_token { get; set; }
        [DataMember] public string scope { get; set; }
    }

    [DataContract]
    internal class DO_Submission
    {
        [DataMember] public string upload_url { get; set; }
        [DataMember] public string submission_id { get; set; }
    }

    public class ResistantAIConnector
    {
        public const string RAI_URL_TOKEN = "RAI-URL-TOKEN";
        public const string RAI_URL_API = "RAI-URL-API";
        public const string RAI_CLIENT_ID = "RAI-CLIENT-ID";
        public const string RAI_CLIENT_SECRET = "RAI-CLIENT-SECRET";
        public const string RAI_CLIENT_TOKEN = "RAI-CLIENT-TOKEN";
        public const string RAI_ENABLE_DECISION = "RAI-ENABLE-DECISION";
        public const string RAI_ENABLE_SUBMISSION_CHARACTERISTICS = "RAI-ENABLE-SUBMISSION-CHARACTERISTICS";

        // Still present for backwards compatibility / internal use,
        // but NOT used by the "no-proxy" entry points anymore.
        public const string RAI_PROXY_ENABLE = "RAI-PROXY-ENABLE";
        public const string RAI_PROXY_URL = "RAI-PROXY-URL";
        public const string RAI_PROXY_USERNAME = "RAI-PROXY-USERNAME";
        public const string RAI_PROXY_PASSWORD = "RAI-PROXY-PASSWORD";

        private IWebProxy _cachedProxy = null;

        private DO_AuthCodeParamteres AuthToken { get; set; }

        // =========================================================
        // NEW: build proxy from caller-provided settings
        // =========================================================
        private IWebProxy BuildProxyFromSettings(DO_ProxySettings settings)
        {
            if (settings == null)
                return null;



            if (string.IsNullOrWhiteSpace(settings.Url))
                return null;

            WebProxy proxy = new WebProxy(settings.Url)
            {
                BypassProxyOnLocal = false,
                BypassList = Array.Empty<string>()
            };

            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                proxy.Credentials = new NetworkCredential(settings.Username, settings.Password ?? string.Empty);
            }
            else
            {
                // If no username provided, fallback to default credentials (matches your existing behavior)
                proxy.UseDefaultCredentials = true;
            }

            return proxy;
        }

        private DO_AuthCodeParamteres RefreshAuthToken(string URL, string ClientID, string ClientSecret)
        {
            string credentials = string.Format("{0}:{1}", ClientID, ClientSecret);
            string base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));

            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(URL);
            httpWebRequest.ContentType = "application/x-www-form-urlencoded";
            httpWebRequest.Accept = "application/json";
            httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "Basic " + base64Credentials);
            httpWebRequest.Method = "POST";

            if (_cachedProxy != null)
                httpWebRequest.Proxy = _cachedProxy;

            string requestBody = "grant_type=client_credentials&scope=submissions.read submissions.write";
            byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
            httpWebRequest.ContentLength = requestBodyBytes.Length;

            using (var requestStream = httpWebRequest.GetRequestStream())
            {
                requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                requestStream.Flush();
            }

            string responseText;
            HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            var encoding = ASCIIEncoding.UTF8;

            using (var reader = new StreamReader(httpWebResponse.GetResponseStream(), encoding))
            {
                responseText = reader.ReadToEnd();
            }

            DO_AuthCodeParamteres tokenObj;
            using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(responseText)))
            {
                DataContractJsonSerializer deserializer = new DataContractJsonSerializer(typeof(DO_AuthCodeParamteres));
                tokenObj = (DO_AuthCodeParamteres)deserializer.ReadObject(ms);
            }

            return tokenObj;
        }

        // =========================================================
        // Existing TA proxy retrieval (kept, but not used by no-proxy entry points)
        // =========================================================
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

        private DO_AuthCodeParamteres GetTokenFromTA(string taSessionId, string taSdkUrl)
        {
            List<string> vars = new List<string>() { RAI_CLIENT_TOKEN };
            ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
            var sv = serverVariableHelper.GetServerVariables(taSessionId, taSdkUrl, vars);

            return new DO_AuthCodeParamteres() { access_token = sv[RAI_CLIENT_TOKEN].Value };
        }

        private DO_Submission Submission(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret,
                                         string taSessionId, string taSdkUrl, string queryId, bool enableSubmissionCharacteristics = false)
        {
            bool shouldRetry = false;
            do
            {
                try
                {
                    shouldRetry = false;
                    bool enableDecisionValue = RetrieveEnableDecisionValue(taSessionId, taSdkUrl);

                    HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(SubmissionURL);
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Accept = "*/*";
                    httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, string.Format("Bearer {0}", AuthToken.access_token));
                    httpWebRequest.Method = "POST";

                    if (_cachedProxy != null)
                        httpWebRequest.Proxy = _cachedProxy;

                    string requestBody = $@"{{
                        ""query_id"": ""{queryId}"",
                        ""pipeline_configuration"": ""FRAUD_ONLY"",
                        ""enable_decision"": {enableDecisionValue.ToString().ToLower()},
                        ""enable_submission_characteristics"": {enableSubmissionCharacteristics.ToString().ToLower()}
                    }}";

                    byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
                    httpWebRequest.ContentLength = requestBodyBytes.Length;

                    using (var requestStream = httpWebRequest.GetRequestStream())
                    {
                        requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                        requestStream.Flush();
                    }

                    string responseText;
                    DO_Submission submissionObj;

                    HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    var encoding = ASCIIEncoding.UTF8;
                    using (var reader = new StreamReader(httpWebResponse.GetResponseStream(), encoding))
                    {
                        responseText = reader.ReadToEnd();
                    }

                    using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(responseText)))
                    {
                        DataContractJsonSerializer deserializer = new DataContractJsonSerializer(typeof(DO_Submission));
                        submissionObj = (DO_Submission)deserializer.ReadObject(ms);
                    }

                    return submissionObj;
                }
                catch (WebException ex) when (ex.Status == WebExceptionStatus.ProtocolError && ex.Response is HttpWebResponse httpResponse)
                {
                    if ((httpResponse.StatusCode == HttpStatusCode.Unauthorized) || (httpResponse.StatusCode == HttpStatusCode.Forbidden))
                    {
                        shouldRetry = true;
                        AuthToken = RefreshAuthToken(AuthenticationURL, ClientID, ClientSecret);

                        List<string> vars = new List<string>() { RAI_CLIENT_TOKEN };
                        ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                        var dict = serverVariableHelper.GetServerVariables(taSessionId, taSdkUrl, vars);

                        dict[RAI_CLIENT_TOKEN] = new KeyValuePair<string, string>(dict[RAI_CLIENT_TOKEN].Key, AuthToken.access_token);
                        Dictionary<string, string> newDict = dict.ToDictionary(kvp => kvp.Value.Key, kvp => kvp.Value.Value);
                        serverVariableHelper.UpdateServerVariables(newDict, taSessionId, taSdkUrl);
                    }
                    else
                    {
                        throw new WebException($"HTTP Error: {httpResponse.StatusCode}", ex);
                    }
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse httpResponse &&
                                             httpResponse.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
                {
                    throw new Exception("Proxy authentication failed (HTTP 407). Please verify proxy credentials.", ex);
                }
                catch (WebException ex)
                {
                    string responseError = string.Empty;

                    if (ex.Response is HttpWebResponse errorResponse)
                    {
                        using (var stream = errorResponse.GetResponseStream())
                        using (var reader = new StreamReader(stream))
                        {
                            responseError = reader.ReadToEnd();
                        }
                    }

                    throw new WebException($"An error occurred: {responseError}", ex);
                }
            } while (shouldRetry);

            return null;
        }

        private bool RetrieveEnableDecisionValue(string taSessionId, string taSdkUrl)
        {
            try
            {
                List<string> vars = new List<string>() { RAI_ENABLE_DECISION };
                ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                var sv = serverVariableHelper.GetServerVariables(taSessionId, taSdkUrl, vars);

                if (sv.ContainsKey(RAI_ENABLE_DECISION))
                    return sv[RAI_ENABLE_DECISION].Value.ToLower() == "true";

                return false;
            }
            catch
            {
                return false;
            }
        }


        private bool RetrieveEnableSubmissionCharacteristicsValue(string taSessionId, string taSdkUrl)
        {
            try
            {
                List<string> vars = new List<string>()
        {
            RAI_ENABLE_SUBMISSION_CHARACTERISTICS
        };

                ServerVariableHelper helper = new ServerVariableHelper();
                var sv = helper.GetServerVariables(taSessionId, taSdkUrl, vars);

                string value = sv[RAI_ENABLE_SUBMISSION_CHARACTERISTICS].Value;

                return value != null && value.ToLower() == "true";
            }
            catch
            {
                // Default to false if not found or error
                return false;
            }
        }

        private byte[] GetKTADocumentFile(string docID, string ktaSDKUrl, string sessionID)
        {
            byte[] result = new byte[1];
            byte[] buffer = new byte[4096];

            var KTAGetDocumentFile = ktaSDKUrl + "/CaptureDocumentService.svc/json/GetDocumentFile2";
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);

            httpWebRequest.Proxy = null;
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "POST";

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                string json = "{\"sessionId\":\"" + sessionID + "\",\"reportingData\": {\"Station\": \"\", \"MarkCompleted\": false }, \"documentId\":\"" + docID + "\", \"documentFileOptions\": { \"FileType\": \"\", \"IncludeAnnotations\": 0 } }";
                streamWriter.Write(json);
                streamWriter.Flush();
            }

            HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();


            using (Stream responseStream = httpWebResponse.GetResponseStream())
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

            return result;
        }

        /// <summary>
        /// Attempts to retrieve the document's true source file via TA's GetSourceFile API.
        /// Returns null if a source file does not exist for this document (this is expected/by-design
        /// for image documents, and for documents split during Validation/Verification) — callers
        /// should fall back to GetKTADocumentFile in that case, not treat it as a hard failure.
        /// Per TA docs, GetSourceFile returns the file with redactions applied if redactions exist
        /// and the document is a PDF.
        /// </summary>
        private byte[] GetSourceFileFromTA(string docID, string ktaSDKUrl, string sessionID)
        {
            var url = ktaSDKUrl + "/CaptureDocumentService.svc/json/GetSourceFile";
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);

            httpWebRequest.Proxy = null;
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "POST";

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                string json = "{\"sessionId\":\"" + sessionID + "\",\"reportingData\": {\"Station\": \"\", \"MarkCompleted\": false }, \"documentId\":\"" + docID + "\" }";
                streamWriter.Write(json);
                streamWriter.Flush();
            }

            try
            {
                using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
                using (Stream responseStream = httpWebResponse.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    JObject json = JObject.Parse(body);
                    // TA wraps the payload in a "d" property for this SDK style.
                    JToken payload = json["d"] ?? json;

                    JToken sourceFileToken = payload["SourceFile"];
                    if (sourceFileToken == null || sourceFileToken.Type == JTokenType.Null)
                        return null;

                    // Observed shape: a raw JSON array of byte values, e.g. [37,80,68,70,...].
                    // Handle a base64 string shape too, in case a future TA version serializes differently.
                    if (sourceFileToken.Type == JTokenType.Array)
                    {
                        var bytes = sourceFileToken.ToObject<byte[]>();
                        return (bytes != null && bytes.Length > 0) ? bytes : null;
                    }
                    if (sourceFileToken.Type == JTokenType.String)
                    {
                        string base64 = sourceFileToken.ToString();
                        if (string.IsNullOrWhiteSpace(base64)) return null;
                        try { return Convert.FromBase64String(base64); }
                        catch (FormatException) { return null; }
                    }

                    return null;
                }
            }
            catch (WebException wex) when (wex.Response is HttpWebResponse errResponse)
            {
                // "A source file for this document does not exist." (ErrorCode -2147198781) is expected
                // for image documents / split documents — treat as "no source file available", not a failure.
                // Any other error also falls back silently here; UploadFiles' existing GetKTADocumentFile
                // path is the proven fallback and should not be blocked by an unexpected GetSourceFile issue.
                return null;
            }
            catch
            {
                // Same reasoning as above — any unexpected issue with GetSourceFile falls back
                // to the existing, proven GetDocumentFile2 path rather than failing the upload outright.
                return null;
            }
        }

        /// <summary>
        /// Retrieves the bytes to upload to RAI, preferring the true original document via
        /// GetSourceFile (reliable for PDFs regardless of what Document Conversion / PDF Generation
        /// have run) and falling back to the existing GetDocumentFile2-based logic when a source
        /// file is not available (expected for image documents).
        /// </summary>
        private byte[] GetOriginalDocumentBytes(string docID, string ktaSDKUrl, string sessionID)
        {
            byte[] sourceFileBytes = GetSourceFileFromTA(docID, ktaSDKUrl, sessionID);
            if (sourceFileBytes != null && sourceFileBytes.Length > 0)
                return sourceFileBytes;

            return GetKTADocumentFile(docID, ktaSDKUrl, sessionID);
        }

        private string[] UploadFiles(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret,
                                     string QueryId, string DocID, string TASDKURL, string TASession, string characteristicsJson = null,
                                     bool enableSubmissionCharacteristics = false)
        {
            EnsureValidToken(AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL);
            DO_Submission objSubmission = new DO_Submission();
            string characteristicsStatus = "";

            try
            {

                objSubmission = Submission(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, TASession, TASDKURL, QueryId, enableSubmissionCharacteristics);

                if (enableSubmissionCharacteristics)
                {
                    if (string.IsNullOrWhiteSpace(characteristicsJson))
                        throw new Exception("RAI-ENABLE-SUBMISSION-CHARACTERISTICS is true but no characteristics JSON was provided. Document will not be analyzed by RAI.");

                    SubmitSubmissionCharacteristicsAsync(AuthenticationURL, SubmissionURL, objSubmission.submission_id,
                        characteristicsJson, TASDKURL, TASession, ClientID, ClientSecret).GetAwaiter().GetResult();

                    characteristicsStatus = "Submission Characteristics submitted successfully.";
                }

                byte[] FileArray = GetOriginalDocumentBytes(DocID, TASDKURL, TASession);

                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(objSubmission.upload_url);
                httpWebRequest.ContentType = "application/octet-stream";
                httpWebRequest.ContentLength = FileArray.Length;
                httpWebRequest.Method = "PUT";

                if (_cachedProxy != null)
                    httpWebRequest.Proxy = _cachedProxy;

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.BaseStream.Write(FileArray, 0, FileArray.Length);
                    streamWriter.Flush();
                }

                HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();

                string[] Returnarray = { httpWebResponse.StatusCode.ToString(), httpWebResponse.StatusDescription.ToString(), objSubmission.submission_id, characteristicsStatus };
                return Returnarray;
            }
            catch (Exception e)
            {
                string[] arrayError = { "ERROR", e.ToString(), objSubmission.submission_id, string.IsNullOrEmpty(characteristicsStatus) ? "Submission Characteristics status unknown due to error." : characteristicsStatus };
                return arrayError;
            }
        }

        private async Task<string> SubmitSubmissionCharacteristicsAsync(string AuthenticationURL, string submissionUrl, string submissionId, string characteristicsJson, string TASDKURL, string TASession, string ClientID, string ClientSecret)
        {
            if (string.IsNullOrWhiteSpace(submissionId))
                throw new ArgumentException("submissionId is required.");

            if (string.IsNullOrWhiteSpace(characteristicsJson))
                throw new ArgumentException("characteristicsJson is required.");

            // Clean JSON once before sending
            characteristicsJson = CleanCharacteristicsJson(characteristicsJson);

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

                string requestUri = $"{submissionUrl}/{submissionId}/characteristics";
                bool shouldRetry = false;
                bool hasRetried = false;

                do
                {
                    try
                    {
                        shouldRetry = false;

                        using (var content = new StringContent(characteristicsJson, Encoding.UTF8, "application/json"))
                        {
                            HttpResponseMessage response = await httpClient.PutAsync(requestUri, content).ConfigureAwait(false);

                            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                                response.StatusCode == HttpStatusCode.Forbidden)
                            {
                                if (hasRetried)
                                    throw new Exception("Submission Characteristics API authentication failed after token refresh.");

                                shouldRetry = true;
                                hasRetried = true;

                                AuthToken = RefreshAuthToken(AuthenticationURL, ClientID, ClientSecret);

                                ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                                var dict = serverVariableHelper.GetServerVariables(TASession, TASDKURL, new List<string>() { RAI_CLIENT_TOKEN });
                                dict[RAI_CLIENT_TOKEN] = new KeyValuePair<string, string>(
                                    dict[RAI_CLIENT_TOKEN].Key, AuthToken.access_token);
                                serverVariableHelper.UpdateServerVariables(
                                    dict.ToDictionary(kvp => kvp.Value.Key, kvp => kvp.Value.Value), TASession, TASDKURL);

                                httpClient.DefaultRequestHeaders.Authorization =
                                    new AuthenticationHeaderValue("Bearer", AuthToken.access_token);
                            }
                            else if (response.StatusCode == HttpStatusCode.NoContent)
                            {
                                return "SUCCESS";
                            }
                            else
                            {
                                string errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                throw new Exception($"Submission Characteristics API failed. StatusCode={(int)response.StatusCode}. Response={errorContent}");
                            }
                        }
                    }
                    catch (HttpRequestException ex) when (IsProxyAuthError(ex))
                    {
                        throw new Exception("Proxy authentication failed (HTTP 407). Please verify proxy settings.", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new Exception($"Submission Characteristics API request failed: {ex.Message}", ex);
                    }
                } while (shouldRetry);
            }

            return "SUCCESS";
        }



        private async Task<string> FetchResultsAsync(string submissionUrl, string submissionId, int maxAttempts, string AuthenticationURL, string ClientID, string ClientSecret, string TASession, string TASDKURL)
        {
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

                string requestUri = $"{submissionUrl}/{submissionId}/fraud?with_metadata=true";
                string result = null;
                int attempt = 0;
                int delayMs = 4000;

                while (attempt <= maxAttempts && string.IsNullOrWhiteSpace(result))
                {
                    try
                    {
                        await Task.Delay(delayMs).ConfigureAwait(false);

                        HttpResponseMessage response = await httpClient.GetAsync(requestUri).ConfigureAwait(false);

                        if (response.StatusCode == HttpStatusCode.Unauthorized ||
                            response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            AuthToken = RefreshAuthToken(AuthenticationURL, ClientID, ClientSecret);

                            ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                            var dict = serverVariableHelper.GetServerVariables(TASession, TASDKURL, new List<string>() { RAI_CLIENT_TOKEN });
                            dict[RAI_CLIENT_TOKEN] = new KeyValuePair<string, string>(dict[RAI_CLIENT_TOKEN].Key, AuthToken.access_token);
                            serverVariableHelper.UpdateServerVariables(
                                dict.ToDictionary(kvp => kvp.Value.Key, kvp => kvp.Value.Value), TASession, TASDKURL);

                            httpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", AuthToken.access_token);

                            delayMs += 1000 * attempt;
                            attempt++;
                            continue;
                        }

                        response.EnsureSuccessStatusCode();

                        result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch (HttpRequestException ex) when (IsProxyAuthError(ex))
                    {
                        throw new Exception("Proxy authentication failed (HTTP 407). Please verify proxy settings.", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (attempt >= maxAttempts)
                            throw new Exception($"Too many failures (attempt {attempt})", ex);
                    }

                    delayMs += 1000 * attempt;
                    attempt++;
                }

                if (string.IsNullOrWhiteSpace(result))
                    throw new Exception("Could not get the results from ResistantAI API");

                return result;
            }
        }



        private void EnsureValidToken(string AuthenticationURL, string ClientID, string ClientSecret, string TASession, string TASDKURL)
        {
            AuthToken = GetTokenFromTA(TASession, TASDKURL);

            if (AuthToken == null || string.IsNullOrWhiteSpace(AuthToken.access_token))
            {
                AuthToken = RefreshAuthToken(AuthenticationURL, ClientID, ClientSecret);

                if (AuthToken == null || string.IsNullOrWhiteSpace(AuthToken.access_token))
                    throw new Exception("Unable to obtain a valid Auth Token from RAI.");

                ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                var dict = serverVariableHelper.GetServerVariables(TASession, TASDKURL, new List<string>() { RAI_CLIENT_TOKEN });
                dict[RAI_CLIENT_TOKEN] = new KeyValuePair<string, string>(dict[RAI_CLIENT_TOKEN].Key, AuthToken.access_token);
                serverVariableHelper.UpdateServerVariables(
                    dict.ToDictionary(kvp => kvp.Value.Key, kvp => kvp.Value.Value), TASession, TASDKURL);
            }
        }



        private string CleanCharacteristicsJson(string json)
        {
            try
            {
                JToken token = JToken.Parse(json);
                CleanToken(token);

                if (token.Type == JTokenType.Object && !token.HasValues)
                    throw new ArgumentException("characteristicsJson does not contain any populated fields after cleanup.");

                return token.ToString(Formatting.None);
            }
            catch (JsonReaderException ex)
            {
                throw new ArgumentException($"characteristicsJson is not valid JSON: {ex.Message}", ex);
            }
        }

        private void CleanToken(JToken token)
        {
            if (token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                List<string> toRemove = new List<string>();

                foreach (var property in obj.Properties())
                {
                    if (property.Value.Type == JTokenType.Null)
                    {
                        toRemove.Add(property.Name);
                    }
                    else if (property.Value.Type == JTokenType.String &&
                             string.IsNullOrEmpty(property.Value.ToString()))
                    {
                        toRemove.Add(property.Name);
                    }
                    else if (property.Value.Type == JTokenType.Array &&
                             !property.Value.HasValues)
                    {
                        toRemove.Add(property.Name);
                    }
                    else if (property.Value.Type == JTokenType.Object)
                    {
                        CleanToken(property.Value);
                        if (!property.Value.HasValues)
                            toRemove.Add(property.Name);
                    }
                }

                foreach (string key in toRemove)
                    obj.Remove(key);
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (JToken child in token.Children())
                    CleanToken(child);
            }
        }





        private async Task<string[]> FetchAdaptiveResultAsync(string submissionUrl, string submissionId, int maxAttempts, string TASDKURL, string TASession, string AuthenticationURL, string ClientID, string ClientSecret)
        {
            var handler = new HttpClientHandler();

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

                var requestUri = $"{submissionUrl}/{submissionId}/decision";
                string[] result = new string[2];
                int attempt = 0;
                int delayMs = 4000;

                while (attempt <= maxAttempts && string.IsNullOrWhiteSpace(result[0]))
                {
                    try
                    {
                        await Task.Delay(delayMs).ConfigureAwait(false);

                        var response = await httpClient.GetAsync(requestUri).ConfigureAwait(false);
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (response.StatusCode == HttpStatusCode.Unauthorized ||
                            response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            AuthToken = RefreshAuthToken(AuthenticationURL, ClientID, ClientSecret);

                            ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                            var dict = serverVariableHelper.GetServerVariables(TASession, TASDKURL, new List<string>() { RAI_CLIENT_TOKEN });
                            dict[RAI_CLIENT_TOKEN] = new KeyValuePair<string, string>(dict[RAI_CLIENT_TOKEN].Key, AuthToken.access_token);
                            serverVariableHelper.UpdateServerVariables(
                                dict.ToDictionary(kvp => kvp.Value.Key, kvp => kvp.Value.Value), TASession, TASDKURL);

                            httpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", AuthToken.access_token);

                            delayMs += 1000 * attempt;
                            attempt++;
                            continue;
                        }

                        if (response.StatusCode == HttpStatusCode.BadRequest)
                        {
                            var json = JObject.Parse(content);
                            if (json["message"]?.ToString()?.Contains("Adaptive Decision feature enabled") == true)
                            {
                                result[0] = "Adaptive Decision feature was not enabled for this submission.";
                                result[1] = "Adaptive Decision feature was not enabled for this submission.";
                                return result;
                            }
                            else
                            {
                                throw new Exception($"Bad request: {content}");
                            }
                        }

                        response.EnsureSuccessStatusCode();

                        var resultJson = JObject.Parse(content);
                        if (resultJson["decision"] != null)
                        {
                            result[0] = resultJson["decision"]?.ToString();
                            result[1] = resultJson["reason"]?["sub_reason"]?["label"]?.ToString();
                            break;
                        }

                        // RAI can return a structured, non-decision status (e.g. INVALID_INPUT when the
                        // underlying Fraud/forensics result was already invalid, such as a password-protected
                        // document). This is a definitive answer, not a "not ready yet" state — retrying
                        // further would just burn the remaining attempts for no benefit. Surface it directly
                        // instead of the generic "Could not get Adaptive Decision result" fallback below.
                        var status = resultJson["status"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            var message = resultJson["message"]?.ToString();
                            result[0] = status;
                            result[1] = !string.IsNullOrWhiteSpace(message) ? message : status;
                            break;
                        }
                    }
                    catch (HttpRequestException ex) when (IsProxyAuthError(ex))
                    {
                        throw new Exception("Proxy authentication failed (HTTP 407). Please verify proxy settings.", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (attempt >= maxAttempts)
                            throw new Exception($"Too many failures (attempt {attempt})", ex);
                    }

                    delayMs += 1000 * attempt;
                    attempt++;
                }

                if (string.IsNullOrWhiteSpace(result[0]))
                    throw new Exception("Could not get Adaptive Decision result from ResistantAI API.");

                return result;
            }
        }




        /// <summary>
        /// Deletes a submission and its associated data from RAI, per DELETE /v2/submission/{submission_id}.
        /// Handles RAI's documented response codes:
        ///   204/200  — deleted successfully
        ///   401/403  — refresh token and retry (same pattern as the other RAI calls)
        ///   404      — submission not found (already deleted, or never existed) — returned as a
        ///              distinct status rather than treated as success or thrown as an error, so the
        ///              caller/process can decide how to handle it
        ///   409      — submission still being processed; retry with backoff per RAI's guidance
        ///   429      — rate limited; retry with exponential backoff + jitter per RAI's guidance
        /// </summary>
        private async Task<string> DeleteSubmissionAsync(string submissionUrl, string submissionId, int maxAttempts,
            string AuthenticationURL, string ClientID, string ClientSecret, string TASession, string TASDKURL)
        {
            var handler = new HttpClientHandler();
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

                var requestUri = $"{submissionUrl}/{submissionId}";
                int attempt = 0;
                var random = new Random();

                while (attempt <= maxAttempts)
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
                        var response = await httpClient.SendAsync(request).ConfigureAwait(false);

                        if (response.StatusCode == HttpStatusCode.Unauthorized ||
                            response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            AuthToken = RefreshAuthToken(AuthenticationURL, ClientID, ClientSecret);

                            ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
                            var dict = serverVariableHelper.GetServerVariables(TASession, TASDKURL, new List<string>() { RAI_CLIENT_TOKEN });
                            dict[RAI_CLIENT_TOKEN] = new KeyValuePair<string, string>(dict[RAI_CLIENT_TOKEN].Key, AuthToken.access_token);
                            serverVariableHelper.UpdateServerVariables(
                                dict.ToDictionary(kvp => kvp.Value.Key, kvp => kvp.Value.Value), TASession, TASDKURL);

                            httpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", AuthToken.access_token);

                            attempt++;
                            continue;
                        }

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            return "NOT_FOUND";
                        }

                        if (response.StatusCode == HttpStatusCode.Conflict)
                        {
                            // Submission still being processed — retry with a growing delay, matching
                            // the pattern used elsewhere in this connector for transient conditions.
                            if (attempt >= maxAttempts)
                                throw new Exception($"Submission is still being processed and could not be deleted after {attempt} attempts (409 Conflict).");

                            await Task.Delay(2000 + (1000 * attempt)).ConfigureAwait(false);
                            attempt++;
                            continue;
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            // Rate limited — exponential backoff with jitter, per RAI's documented guidance.
                            if (attempt >= maxAttempts)
                                throw new Exception($"Delete request was rate limited (429) after {attempt} attempts.");

                            int backoffMs = (int)Math.Pow(2, attempt) * 1000;
                            int jitterMs = random.Next(0, 500);
                            await Task.Delay(backoffMs + jitterMs).ConfigureAwait(false);
                            attempt++;
                            continue;
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            return "DELETED";
                        }

                        string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new Exception($"Delete failed: HTTP {(int)response.StatusCode} — {errorBody}");
                    }
                    catch (HttpRequestException ex) when (IsProxyAuthError(ex))
                    {
                        throw new Exception("Proxy authentication failed (HTTP 407). Please verify proxy settings.", ex);
                    }
                }

                throw new Exception($"Could not delete submission {submissionId} after {maxAttempts} attempts.");
            }
        }

        // ============================================================
        // ENTRY POINTS - DELETE SUBMISSION
        // ============================================================

        /// <summary>
        /// Deletes a submission from RAI. Returns "DELETED" on success, "NOT_FOUND" if the
        /// submission does not exist (already deleted or never existed), or throws for other
        /// failures (e.g. still processing after all retries, rate limited after all retries).
        /// </summary>
        public string DeleteSubmission(string submissionUrl, string submissionId, int NumberOfRetries,
            string TASDKURL, string TASession, string AuthenticationURL, string ClientID, string ClientSecret)
        {
            _cachedProxy = null;
            EnsureValidToken(AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL);
            return DeleteSubmissionAsync(submissionUrl, submissionId, NumberOfRetries, AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL)
                .GetAwaiter().GetResult();
        }

        /// <summary>Same as DeleteSubmission, but routes the request through the supplied proxy.</summary>
        public string DeleteSubmission1(string submissionUrl, string submissionId, int NumberOfRetries,
            string TASDKURL, string TASession, DO_ProxySettings proxySettings,
            string AuthenticationURL, string ClientID, string ClientSecret)
        {
            _cachedProxy = BuildProxyFromSettings(proxySettings);
            if (_cachedProxy == null)
                throw new ArgumentException("Proxy settings are required for DeleteSubmission1. Provide proxySettings.Enable=true and a valid proxySettings.Url.");
            EnsureValidToken(AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL);
            return DeleteSubmissionAsync(submissionUrl, submissionId, NumberOfRetries, AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL)
                .GetAwaiter().GetResult();
        }

        private bool IsProxyAuthError(HttpRequestException ex)
        {
            var webEx = ex.InnerException as WebException;
            if (webEx == null)
                return false;

            var response = webEx.Response as HttpWebResponse;
            if (response == null)
                return false;

            return response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired;
        }

        // ============================================================
        // ENTRY POINTS - ADAPTIVE RESULT
        // ============================================================

        // Existing entry point: WITHOUT proxy
        public string[] GetAdaptiveResult(string submissionUrl, string submissionId, int NumberOfRetries, string TASDKURL, string TASession, string AuthenticationURL, string ClientID, string ClientSecret)
        {
            _cachedProxy = null;
            EnsureValidToken(AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL);
            return FetchAdaptiveResultAsync(submissionUrl, submissionId, NumberOfRetries, TASDKURL, TASession, AuthenticationURL, ClientID, ClientSecret).GetAwaiter().GetResult();
        }

        // New entry point: WITH proxy (caller provides proxy settings)
        public string[] GetAdaptiveResult1(string submissionUrl, string submissionId, int NumberOfRetries, string TASDKURL, string TASession, DO_ProxySettings proxySettings, string AuthenticationURL, string ClientID, string ClientSecret)
        {
            _cachedProxy = BuildProxyFromSettings(proxySettings);
            if (_cachedProxy == null)
                throw new ArgumentException("Proxy settings are required for GetAdaptiveResult1. Provide proxySettings.Enable=true and a valid proxySettings.Url.");
            EnsureValidToken(AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL);
            return FetchAdaptiveResultAsync(submissionUrl, submissionId, NumberOfRetries, TASDKURL, TASession, AuthenticationURL, ClientID, ClientSecret).GetAwaiter().GetResult();
        }

        // ============================================================
        // Legacy FetchResults (kept as-is)
        // ============================================================
        private string FetchResults(string SubmissionURL, string SubmissionID)
        {
            if (AuthToken.access_token == "")
                throw new Exception("Auth Token is empty!");

            HttpWebRequest httpWebRequest;
            HttpWebResponse httpWebResponse;
            string text = "";
            string requrl = SubmissionURL + "/" + SubmissionID + "/fraud?with_metadata=true";
            int counter = 0;
            int max = 15;
            int delay = 4000;

            while ((counter <= max) && (text.Trim() == ""))
            {
                try
                {
                    Thread.Sleep(delay);
                    httpWebRequest = (HttpWebRequest)WebRequest.Create(requrl);
                    httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, string.Format("Bearer {0}", AuthToken.access_token));
                    httpWebRequest.Method = "GET";
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Accept = "*/*";

                    httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    using (httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
                    using (var sr = new StreamReader(httpWebResponse.GetResponseStream(), ASCIIEncoding.UTF8))
                    {
                        text = sr.ReadToEnd();
                    }
                }
                catch (WebException ex)
                {
                    if (counter >= max)
                        throw new Exception("Too many webexceptions - counter = " + counter + " - ", ex);
                }
                delay = delay + (1000 * counter);
                counter++;
            }

            if (text == "")
                throw new Exception("Could not get the results from ResistantAI API");

            return text;
        }

        // ============================================================
        // ENTRY POINTS - UPLOAD + FETCH FRAUD RESULTS
        // ============================================================

        // Existing entry point: WITHOUT proxy
        public string[] UploadFileAndFetchResultsWithRetries(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret,
                                string QueryId, string DocID, string TASDKURL, string TASession, int NumberOfRetries, out string SuspendReason)
        {
            SuspendReason = "";
            _cachedProxy = null; // no-proxy contract

            string[] uploadresult = UploadFiles(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, QueryId, DocID, TASDKURL, TASession);
            string statusCode = uploadresult[0];
            string statusDesc = uploadresult[1];
            string SubmissionID = uploadresult[2];

            string[] result = new string[2];

            if (statusCode.ToLower() == "error")
            {
                SuspendReason = "Suspended";
                result[0] = statusDesc;
                result[1] = SubmissionID;
            }
            else
            {
                try
                {
                    SuspendReason = "";
                    result[0] = SubmissionID;
                    result[1] = FetchResultsAsync(SubmissionURL, SubmissionID, NumberOfRetries, AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL).GetAwaiter().GetResult();
                }
                catch
                {
                    SuspendReason = "Suspended";
                    throw;
                }
            }

            return result;
        }



        public string[] UploadFileAndFetchResultsWithCharacteristics(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret,
                        string QueryId, string DocID, string TASDKURL, string TASession, int NumberOfRetries,
                        string characteristicsJson, out string Notes)
        {
            Notes = "";
            _cachedProxy = null;

            bool enableSubmissionCharacteristics = RetrieveEnableSubmissionCharacteristicsValue(TASession, TASDKURL);

            if (!enableSubmissionCharacteristics)
                Notes = "Warning: RAI-ENABLE-SUBMISSION-CHARACTERISTICS is false. Submission characteristics will be skipped.";

            string[] uploadresult = UploadFiles(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, QueryId, DocID, TASDKURL, TASession,
                characteristicsJson, enableSubmissionCharacteristics);

            string statusCode = uploadresult[0];
            string statusDesc = uploadresult[1];
            string SubmissionID = uploadresult[2];
            string characteristicsStatus = uploadresult.Length > 3 ? uploadresult[3] : "";

            string[] result = new string[2];

            if (statusCode.ToLower() == "error")
            {
                Notes = string.IsNullOrEmpty(characteristicsStatus) ? statusDesc : $"{statusDesc} | Characteristics: {characteristicsStatus}";
                result[0] = statusDesc;
                result[1] = SubmissionID;
            }
            else
            {
                try
                {
                    result[0] = SubmissionID;
                    result[1] = FetchResultsAsync(SubmissionURL, SubmissionID, NumberOfRetries, AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(characteristicsStatus))
                        Notes = characteristicsStatus;
                }
                catch
                {
                    Notes = "Suspended";
                    throw;
                }
            }

            return result;
        }



        public string[] UploadFileAndFetchResultsWithCharacteristics1(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret,
                        string QueryId, string DocID, string TASDKURL, string TASession, int NumberOfRetries,
                        string characteristicsJson, DO_ProxySettings proxySettings, out string Notes)
        {
            Notes = "";

            _cachedProxy = BuildProxyFromSettings(proxySettings);

            if (_cachedProxy == null)
                throw new ArgumentException("Proxy settings are required for UploadFileAndFetchResultsWithCharacteristics1. Provide proxySettings.Enable=true and a valid proxySettings.Url.");

            bool enableSubmissionCharacteristics = RetrieveEnableSubmissionCharacteristicsValue(TASession, TASDKURL);

            if (!enableSubmissionCharacteristics)
                Notes = "Warning: RAI-ENABLE-SUBMISSION-CHARACTERISTICS is false. Submission characteristics will be skipped.";

            string[] uploadresult = UploadFiles(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, QueryId, DocID, TASDKURL, TASession,
                characteristicsJson, enableSubmissionCharacteristics);

            string statusCode = uploadresult[0];
            string statusDesc = uploadresult[1];
            string SubmissionID = uploadresult[2];
            string characteristicsStatus = uploadresult.Length > 3 ? uploadresult[3] : "";

            string[] result = new string[2];

            if (statusCode.ToLower() == "error")
            {
                Notes = string.IsNullOrEmpty(characteristicsStatus) ? statusDesc : $"{statusDesc} | Characteristics: {characteristicsStatus}";
                result[0] = statusDesc;
                result[1] = SubmissionID;
            }
            else
            {
                try
                {
                    result[0] = SubmissionID;
                    result[1] = FetchResultsAsync(SubmissionURL, SubmissionID, NumberOfRetries, AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(characteristicsStatus))
                        Notes = characteristicsStatus;
                }
                catch
                {
                    Notes = "Suspended";
                    throw;
                }
            }

            return result;
        }


        // New entry point: WITH proxy
        public string[] UploadFileAndFetchResultsWithRetries1(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret,
                                string QueryId, string DocID, string TASDKURL, string TASession, int NumberOfRetries,
                                DO_ProxySettings proxySettings, out string SuspendReason)
        {
            SuspendReason = "";

            _cachedProxy = BuildProxyFromSettings(proxySettings);

            // Enforce proxy presence for proxy entry point
            if (_cachedProxy == null)
                throw new ArgumentException("Proxy settings are required for UploadFileAndFetchResultsWithRetries1. Provide proxySettings.Enable=true and a valid proxySettings.Url.");

            string[] uploadresult = UploadFiles(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, QueryId, DocID, TASDKURL, TASession);
            string statusCode = uploadresult[0];
            string statusDesc = uploadresult[1];
            string SubmissionID = uploadresult[2];

            string[] result = new string[2];

            if (statusCode.ToLower() == "error")
            {
                SuspendReason = "Suspended";
                result[0] = statusDesc;
                result[1] = SubmissionID;
            }
            else
            {
                try
                {
                    SuspendReason = "";
                    result[0] = SubmissionID;
                    result[1] = FetchResultsAsync(SubmissionURL, SubmissionID, NumberOfRetries, AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL).GetAwaiter().GetResult();
                }
                catch
                {
                    SuspendReason = "Suspended";
                    throw;
                }
            }

            return result;
        }

        // Existing non-retry entry point: WITHOUT proxy (adjusted per new rule)
        public string[] UploadFileAndFetchResults(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret, string QueryId, string DocID,
            string TASDKURL, string TASession)
        {
            _cachedProxy = null; // no-proxy contract

            string[] uploadresult = UploadFiles(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, QueryId, DocID, TASDKURL, TASession);
            string statusCode = uploadresult[0];
            string statusDesc = uploadresult[1];
            string SubmissionID = uploadresult[2];

            string[] result = new string[2];

            if (statusCode.ToLower() == "error")
            {
                result[0] = statusDesc;
                result[1] = SubmissionID;
            }
            else
            {
                try
                {
                    result[0] = SubmissionID;
                    result[1] = FetchResultsAsync(SubmissionURL, SubmissionID, 3, AuthenticationURL, ClientID, ClientSecret, TASession, TASDKURL).GetAwaiter().GetResult();
                }
                catch
                {
                    throw;
                }
            }

            return result;
        }
    }
}