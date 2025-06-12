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

    //authentication dataobject
    [DataContract]
    internal class DO_AuthCodeParamteres
    {
        [DataMember]
        public string token_type { get; set; }

        [DataMember]
        public string expires_in { get; set; }

        [DataMember]
        public string access_token { get; set; }

        [DataMember]
        public string scope { get; set; }
    }

    [DataContract]
    internal class DO_Submission
    {
        [DataMember]
        public string upload_url { get; set; }

        [DataMember]
        public string submission_id { get; set; }
    }

    public class ResistantAIConnector
    {
        public const string RAI_URL_TOKEN = "RAI-URL-TOKEN";
        public const string RAI_URL_API = "RAI-URL-API";
        public const string RAI_CLIENT_ID = "RAI-CLIENT-ID";
        public const string RAI_CLIENT_SECRET = "RAI-CLIENT-SECRET";
        public const string RAI_CLIENT_TOKEN = "RAI-CLIENT-TOKEN";
        public const string RAI_ENABLE_DECISION = "RAI-ENABLE-DECISION";

        private DO_AuthCodeParamteres AuthToken { get; set; }

        private DO_AuthCodeParamteres RefreshAuthToken(String URL, String ClientID, String ClientSecret)
        {
            //Setting the URi and calling the get document API
            string credentials = string.Format("{0}:{1}", ClientID, ClientSecret);
            string base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));

            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(URL);
            httpWebRequest.ContentType = "application/x-www-form-urlencoded";
            httpWebRequest.Accept = "application/json";
            httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "Basic " + base64Credentials);
            httpWebRequest.Method = "POST";
            // CONSTRUCT JSON Payload
            string requestBody = "grant_type=client_credentials&scope=submissions.read submissions.write";
            // Convert the request body string to bytes
            byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
            // Set the ContentLength of the request
            httpWebRequest.ContentLength = requestBodyBytes.Length;
            // Write the request body to the request stream
            using (var requestStream = httpWebRequest.GetRequestStream())
            {
                requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                requestStream.Flush();
            }
            //Reading response from API
            String responseText = String.Empty;
            DO_AuthCodeParamteres bsObj2;
            HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            var encoding = ASCIIEncoding.UTF8;
            using (var reader = new System.IO.StreamReader(httpWebResponse.GetResponseStream(), encoding))
            {
                responseText = reader.ReadToEnd();
            }
            //deserialize JSON string to its key value pairs
            using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(responseText)))
            {
                // Deserialization from JSON
                DataContractJsonSerializer deserializer = new DataContractJsonSerializer(typeof(DO_AuthCodeParamteres));
                bsObj2 = (DO_AuthCodeParamteres)deserializer.ReadObject(ms);
            }
            // String[] ReturnParamteres = { bsObj2.access_token, bsObj2.expires_in, bsObj2.token_type, bsObj2.scope };
            return bsObj2;
        }

        private DO_AuthCodeParamteres GetTokenFromTA(string taSessionId, string taSdkUrl)
        {
            List<string> vars = new List<string>() { RAI_CLIENT_TOKEN };
            ServerVariableHelper serverVariableHelper = new ServerVariableHelper();
            var sv = serverVariableHelper.GetServerVariables(taSessionId, taSdkUrl, vars);

            return new DO_AuthCodeParamteres() { access_token = sv[RAI_CLIENT_TOKEN].Value };
        }

        private DO_Submission Submission(String AuthenticationURL, String SubmissionURL, String ClientID, String ClientSecret, string taSessionId, string taSdkUrl)
        {
            bool shouldRetry = false;
            do
            {
                try 
                {
                    shouldRetry = false;
                     bool enableDecisionValue = RetrieveEnableDecisionValue(taSessionId, taSdkUrl);
                    //Setting the URi and calling the get document API
                    //DO_AuthCodeParamteres ReturnParamteres = GetAuthToken(AuthenticationURL, ClientID, ClientSecret);
                    //AuthToken = GetAuthToken(AuthenticationURL, ClientID, ClientSecret);
                    HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(SubmissionURL);
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Accept = "*/*";
                    httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, string.Format("Bearer {0}", AuthToken.access_token));
                    httpWebRequest.Method = "POST";
                    // CONSTRUCT JSON Payload
                    //string requestBody = "{\"query_id\":\"string\",\"pipeline_configuration\":\"FRAUD_ONLY\",\"enable_decision\":{enableDecisionValue.ToString().ToLower()},\"enable_submission_characteristics\":false}";

                    string requestBody = $@"{{
                        ""query_id"": ""string"",
                        ""pipeline_configuration"": ""FRAUD_ONLY"",
                        ""enable_decision"": {enableDecisionValue.ToString().ToLower()},
                        ""enable_submission_characteristics"": false
                    }}";


                    // Convert the request body string to bytes
                    byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
                    // Set the ContentLength of the request
                    httpWebRequest.ContentLength = requestBodyBytes.Length;
                    // Write the request body to the request stream
                    using (var requestStream = httpWebRequest.GetRequestStream())
                    {
                        requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                        requestStream.Flush();
                    }
                    //Reading response from API
                    String responseText = String.Empty;
                    DO_Submission bsObj2;
                    HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    var encoding = ASCIIEncoding.UTF8;
                    using (var reader = new System.IO.StreamReader(httpWebResponse.GetResponseStream(), encoding))
                    {
                        responseText = reader.ReadToEnd();
                    }
                    //deserialize JSON string to its key value pairs
                    using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(responseText)))
                    {
                        // Deserialization from JSON
                        DataContractJsonSerializer deserializer = new DataContractJsonSerializer(typeof(DO_Submission));
                        bsObj2 = (DO_Submission)deserializer.ReadObject(ms);
                    }
                    //String[] ReturnParamteres = { bsObj2.access_token, bsObj2.expires_in, bsObj2.token_type, bsObj2.scope };
                    return bsObj2;
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
                {
                    return sv[RAI_ENABLE_DECISION].Value.ToLower() == "true";
                }
                else
                {
                    return false; // Default value if not found
                }

            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private DO_Submission UploadFile(String AuthenticationURL, String SubmissionURL, String ClientID, String ClientSecret)
        {
            //Setting the URi and calling the get document API
            //DO_AuthCodeParamteres ReturnParamteres = GetAuthToken(AuthenticationURL, ClientID, ClientSecret);
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(SubmissionURL);
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Accept = "*/*";
            //ktaHttpWebRequest.Connection = "keep-alive";
            httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, string.Format("Bearer {0}", AuthToken.access_token));
            httpWebRequest.Method = "POST";
            // CONSTRUCT JSON Payload
            string requestBody = "{\"query_id\":\"string\",\"pipeline_configuration\":\"FRAUD_ONLY\",\"enable_decision\":false,\"enable_submission_characteristics\":false}";

            // Convert the request body string to bytes
            byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
            // Set the ContentLength of the request
            httpWebRequest.ContentLength = requestBodyBytes.Length;
            // Write the request body to the request stream
            using (var requestStream = httpWebRequest.GetRequestStream())
            {
                requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                requestStream.Flush();
            }
            //Reading response from API
            String responseText = String.Empty;
            DO_Submission bsObj2;
            HttpWebResponse ktaHttpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            var encoding = ASCIIEncoding.UTF8;
            using (var reader = new System.IO.StreamReader(ktaHttpWebResponse.GetResponseStream(), encoding))
            {
                responseText = reader.ReadToEnd();
            }
            //deserialize JSON string to its key value pairs
            using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(responseText)))
            {
                // Deserialization from JSON
                DataContractJsonSerializer deserializer = new DataContractJsonSerializer(typeof(DO_Submission));
                bsObj2 = (DO_Submission)deserializer.ReadObject(ms);
            }
            //String[] ReturnParamteres = { bsObj2.access_token, bsObj2.expires_in, bsObj2.token_type, bsObj2.scope };
            return bsObj2;
        }

        private byte[] GetKTADocumentFile(string docID, string ktaSDKUrl, string sessionID)
        {
            byte[] result = new byte[1];
            byte[] buffer = new byte[4096];
            //string fileType = "pdf";
            string status = "OK";

 //           try
 //           {
                //Setting the URi and calling the get document API
                var KTAGetDocumentFile = ktaSDKUrl + "/CaptureDocumentService.svc/json/GetDocumentFile2";
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";

                // CONSTRUCT JSON Payload
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    string json = "{\"sessionId\":\"" + sessionID + "\",\"reportingData\": {\"Station\": \"\", \"MarkCompleted\": false }, \"documentId\":\"" + docID + "\", \"documentFileOptions\": { \"FileType\": \"\", \"IncludeAnnotations\": 0 } }";
                    streamWriter.Write(json);
                    streamWriter.Flush();
                }

                HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                Stream receiveStream = httpWebResponse.GetResponseStream();
                Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
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
            //}
            //catch (Exception ex)
            //{
            //    status = "An error occured: " + ex.ToString();
            //    return result;
            //}
        }

        private String[] UploadFiles(String AuthenticationURL, String SubmissionURL, String ClientID, String ClientSecret, String DocID, String TASDKURL, String TASession)
        {
            AuthToken = GetTokenFromTA(TASession, TASDKURL);
            DO_Submission objSubmission = new DO_Submission();
            try
            {   //calling authorization and submission API CAlls and get SubmissionID and UploadURl in return
                objSubmission = Submission(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, TASession, TASDKURL);
                byte[] FileArray = GetKTADocumentFile(DocID, TASDKURL, TASession);
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(objSubmission.upload_url);
                httpWebRequest.ContentType = "application/octet-stream";
                httpWebRequest.ContentLength = FileArray.Length;
                httpWebRequest.Method = "PUT";
                // CONSTRUCT JSON Payload
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.BaseStream.Write(FileArray, 0, FileArray.Length);
                    streamWriter.Flush();
                }
                //Reading response from API
                String responseText = String.Empty;
                HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                var encoding = ASCIIEncoding.UTF8;
                using (var reader = new System.IO.StreamReader(httpWebResponse.GetResponseStream(), encoding))
                {
                    responseText = reader.ReadToEnd();
                }
                //Return ARray response = OK, OK, SubmissionID
                string[] Returnarray = { httpWebResponse.StatusCode.ToString(), httpWebResponse.StatusDescription.ToString(), objSubmission.submission_id };
                return Returnarray;
            }
            catch (Exception e)
            {
                
                string[] arrayError = { "ERROR", e.ToString(), objSubmission.submission_id };
                return arrayError;
            }
        }

        private async Task<string> FetchResultsAsync(string submissionUrl, string submissionId, int maxAttempts)
        {
            HttpClient httpClient = new HttpClient();

            if (string.IsNullOrEmpty(AuthToken.access_token))
                throw new Exception("Auth Token is empty!");

            // set up authorization header once
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", AuthToken.access_token);

            var requestUri = $"{submissionUrl}/{submissionId}/fraud?with_metadata=true";
            string result = null;
            int attempt = 0;
            int delayMs = 4000;

            while (attempt <= maxAttempts && string.IsNullOrWhiteSpace(result))
            {
                try
                {
                    // async wait instead of Thread.Sleep
                    await Task.Delay(delayMs).ConfigureAwait(false);

                    var response = await httpClient.GetAsync(requestUri).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode(); // throws on non-2xx

                    result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= maxAttempts)
                        throw new Exception($"Too many failures (attempt {attempt})", ex);
                    // else swallow and retry
                }

                // linear back-off
                delayMs += 1000 * attempt;
                attempt++;
            }

            if (string.IsNullOrWhiteSpace(result))
                throw new Exception("Could not get the results from ResistantAI API");

            return result;
        }





        private async Task<string[]> FetchAdaptiveResultAsync(string submissionUrl, string submissionId, int maxAttempts, String TASDKURL, String TASession)
        {
            HttpClient httpClient = new HttpClient();
            AuthToken = GetTokenFromTA(TASession, TASDKURL);
            if (string.IsNullOrEmpty(AuthToken.access_token))
                throw new Exception("Auth Token is empty!");

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
                        break; // Got result, exit loop
                    }
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



        public string[] GetAdaptiveResult(string submissionUrl, string submissionId, String TASDKURL, String TASession)
        {
            return FetchAdaptiveResultAsync(submissionUrl, submissionId, 3, TASDKURL, TASession).GetAwaiter().GetResult();
        }





        private string FetchResults(string SubmissionURL, string SubmissionID)
        {
            if (AuthToken.access_token == "")
            {
                throw new Exception("Auth Token is empty!");
            }
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
                    // Handle potential web exceptions (e.g., connection errors, 404)
                    if (counter >= max) // If it's the last attempt, rethrow the exception
                    {
                        throw new Exception("Too many webexceptions - counter = " + counter + " - ", ex);
                    }
                }
                delay = delay + (1000 * counter);
                counter++;
            }

            if (text == "") throw new Exception("Could not get the results from ResistantAI API");

            return text;
        }

        public string[] UploadFileAndFetchResultsWithRetries(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret, string DocID, string TASDKURL, string TASession, int NumberOfRetries, out string SuspendReason)
        {
            SuspendReason = "";
            string[] uploadresult = UploadFiles(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, DocID, TASDKURL, TASession);
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
                    //result[1] = FetchResults(SubmissionURL, SubmissionID);
                    result[1] = FetchResultsAsync(SubmissionURL, SubmissionID, NumberOfRetries).GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    SuspendReason = "Suspended";
                    throw e;
                }
                
            }

            return result;
        }

        public string[] UploadFileAndFetchResults(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret, string DocID, string TASDKURL, string TASession)
        {
            string[] uploadresult = UploadFiles(AuthenticationURL, SubmissionURL, ClientID, ClientSecret, DocID, TASDKURL, TASession);
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
                    //result[1] = FetchResults(SubmissionURL, SubmissionID);
                    result[1] = FetchResultsAsync(SubmissionURL, SubmissionID,1).GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    throw e;
                }

            }

            return result;
        }
    }
}