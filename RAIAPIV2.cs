using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Net.Http;

namespace RAIV2DLL
{

    //authentication dataobject
    [DataContract]
    class DO_AuthCodeParamteres
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
    class DO_Submission
    {
        [DataMember]
        public string upload_url { get; set; }

        [DataMember]
        public string submission_id { get; set; }
    }

    public class RAIAPIV2
    {

        private DO_AuthCodeParamteres GetAuthToken(String URL, String AuthCode)
        {
            //Setting the URi and calling the get document API
            var KTAGetDocumentFile = URL;
            HttpWebRequest ktaHttpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);
            ktaHttpWebRequest.ContentType = "application/x-www-form-urlencoded";
            ktaHttpWebRequest.Accept = "application/json";
            ktaHttpWebRequest.Headers.Add(HttpRequestHeader.Authorization, string.Format("Basic {0}", AuthCode));
            ktaHttpWebRequest.Method = "POST";
            // CONSTRUCT JSON Payload         
            string requestBody = "grant_type=client_credentials&scope=submissions.read submissions.write";
            // Convert the request body string to bytes
            byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
            // Set the ContentLength of the request
            ktaHttpWebRequest.ContentLength = requestBodyBytes.Length;
            // Write the request body to the request stream
            using (var requestStream = ktaHttpWebRequest.GetRequestStream())
            {
                requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                requestStream.Flush();
            }
            //Reading response from API
            String responseText = String.Empty;
            DO_AuthCodeParamteres bsObj2;
            HttpWebResponse ktaHttpWebResponse = (HttpWebResponse)ktaHttpWebRequest.GetResponse();
            var encoding = ASCIIEncoding.UTF8;
            using (var reader = new System.IO.StreamReader(ktaHttpWebResponse.GetResponseStream(), encoding))
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
        private DO_Submission Submission(String AuthenticationURL, String SubmissionURL, String AuthCode)
        {
            //Setting the URi and calling the get document API
            DO_AuthCodeParamteres ReturnParamteres = GetAuthToken(AuthenticationURL, AuthCode);
            var KTAGetDocumentFile = SubmissionURL;
            HttpWebRequest ktaHttpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);
            ktaHttpWebRequest.ContentType = "application/json";
            ktaHttpWebRequest.Accept = "*/*";
            //ktaHttpWebRequest.Connection = "keep-alive";
            ktaHttpWebRequest.Headers.Add(HttpRequestHeader.Authorization, string.Format("Bearer {0}", ReturnParamteres.access_token));
            ktaHttpWebRequest.Method = "POST";
            // CONSTRUCT JSON Payload         
            string requestBody = "{\"query_id\":\"string\",\"pipeline_configuration\":\"FRAUD_ONLY\",\"enable_decision\":false,\"enable_submission_characteristics\":false}";

            // Convert the request body string to bytes
            byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
            // Set the ContentLength of the request
            ktaHttpWebRequest.ContentLength = requestBodyBytes.Length;
            // Write the request body to the request stream
            using (var requestStream = ktaHttpWebRequest.GetRequestStream())
            {
                requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                requestStream.Flush();
            }
            //Reading response from API
            String responseText = String.Empty;
            DO_Submission bsObj2;
            HttpWebResponse ktaHttpWebResponse = (HttpWebResponse)ktaHttpWebRequest.GetResponse();
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
        private DO_Submission UploadFile(String AuthenticationURL, String SubmissionURL, String AuthCode)
        {
            //Setting the URi and calling the get document API
            DO_AuthCodeParamteres ReturnParamteres = GetAuthToken(AuthenticationURL, AuthCode);
            var KTAGetDocumentFile = SubmissionURL;
            HttpWebRequest ktaHttpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);
            ktaHttpWebRequest.ContentType = "application/json";
            ktaHttpWebRequest.Accept = "*/*";
            //ktaHttpWebRequest.Connection = "keep-alive";
            ktaHttpWebRequest.Headers.Add(HttpRequestHeader.Authorization, string.Format("Bearer {0}", ReturnParamteres.access_token));
            ktaHttpWebRequest.Method = "POST";
            // CONSTRUCT JSON Payload         
            string requestBody = "{\"query_id\":\"string\",\"pipeline_configuration\":\"FRAUD_ONLY\",\"enable_decision\":false,\"enable_submission_characteristics\":false}";

            // Convert the request body string to bytes
            byte[] requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
            // Set the ContentLength of the request
            ktaHttpWebRequest.ContentLength = requestBodyBytes.Length;
            // Write the request body to the request stream
            using (var requestStream = ktaHttpWebRequest.GetRequestStream())
            {
                requestStream.Write(requestBodyBytes, 0, requestBodyBytes.Length);
                requestStream.Flush();
            }
            //Reading response from API
            String responseText = String.Empty;
            DO_Submission bsObj2;
            HttpWebResponse ktaHttpWebResponse = (HttpWebResponse)ktaHttpWebRequest.GetResponse();
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
        public String[] UploadFiles(String AuthenticationURL, String SubmissionURL, String AuthCode, String Base64)
        {
            DO_Submission objSubmission = new DO_Submission();
            try
            {   //calling authorization and submission API CAlls and get SubmissionID and UploadURl in return
                objSubmission = Submission(AuthenticationURL, SubmissionURL, AuthCode);
                byte[] FileArray = Convert.FromBase64String(Base64);
                var KTAGetDocumentFile = objSubmission.upload_url;
                HttpWebRequest ktaHttpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);
                ktaHttpWebRequest.ContentType = "application/octet-stream";
                ktaHttpWebRequest.ContentLength = FileArray.Length;
                ktaHttpWebRequest.Method = "PUT";
                // CONSTRUCT JSON Payload
                using (var streamWriter = new StreamWriter(ktaHttpWebRequest.GetRequestStream()))
                {
                    streamWriter.BaseStream.Write(FileArray, 0, FileArray.Length);
                    streamWriter.Flush();
                }
                //Reading response from API
                String responseText = String.Empty;
                HttpWebResponse ktaHttpWebResponse = (HttpWebResponse)ktaHttpWebRequest.GetResponse();
                var encoding = ASCIIEncoding.UTF8;
                using (var reader = new System.IO.StreamReader(ktaHttpWebResponse.GetResponseStream(), encoding))
                {
                    responseText = reader.ReadToEnd();
                }
                //Return ARray response = OK, OK, SubmissionID
                string[] Returnarray = { ktaHttpWebResponse.StatusCode.ToString(), ktaHttpWebResponse.StatusDescription.ToString(), objSubmission.submission_id };
                return Returnarray;
            }
            catch (Exception e)
            {
                string[] arrayError = { "ERROR", e.Message.ToString(), objSubmission.submission_id };
                return arrayError;
            }
        }
    }
}
