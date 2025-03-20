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
        private DO_AuthCodeParamteres AuthToken { get; set; }
        
        public string GetDocumentWithBoundingBoxes(String AuthenticationURL, String SubmissionURL, String ClientID, String ClientSecret, String DocID, String TASDKURL, String TASession, String SubmissionID, String Category)
        {
            string documentIdWithBoundingBoxes = string.Empty;

            AuthToken = GetAuthToken(AuthenticationURL, ClientID, ClientSecret);

            return FetchResultsWithMetadata(SubmissionURL, DocID, TASDKURL, TASession, SubmissionID, Category);
            //return CreateTADocument(imageWithBoxes,TASDKURL, TASession);

        }

        private DO_AuthCodeParamteres GetAuthToken(String URL, String ClientID, String ClientSecret)
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



        private string FetchResultsWithMetadata(String SubmissionURL, String DocID, String TASDKURL, String TASession, String SubmissionID, String Category)
        {
            if (AuthToken.access_token == "")
            {
                throw new Exception("Auth Token is empty!");
            }

            HttpWebRequest httpWebRequest;
            HttpWebResponse httpWebResponse;
            string text = "";
            string requrl = $"{SubmissionURL}/{SubmissionID}/fraud?with_metadata=true";  // Add the with_metadata=true parameter
            int counter = 0;
            int max = 15;
            int delay = 4000;

            while (counter <= max && string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    Thread.Sleep(delay);
                    httpWebRequest = (HttpWebRequest)WebRequest.Create(requrl);
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
                    // Handle potential web exceptions
                    if (counter >= max)
                    {
                        throw new Exception($"Too many WebExceptions after {counter} retries - {ex.Message}", ex);
                    }
                }
                catch (Exception ex)
                {
                    // Handle other types of exceptions
                    throw new Exception($"An unexpected error occurred: {ex.Message}", ex);
                }

                delay += (1000 * counter); // Increase delay dynamically
                counter++;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception("Could not get the results from ResistantAI API.");
            }

            List<ContentHidingEntry> CoordinatesList = ExtractContentHidingEntries(text, Category);


            return DrawBoundingBoxesOnImage(DocID, CoordinatesList, TASDKURL, TASession);

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

                                // ✅ Case 1: bbox is a **single object**
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
                                // ✅ Case 2: bbox is an **array of objects**
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
                Console.WriteLine("JSON Parsing Error: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected Error: " + ex.Message);
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
                    image.SelectActiveFrame(frameDimension, i); // Select page

                    Bitmap pageBitmap = new Bitmap(image); // Copy current page
                    using (Graphics g = Graphics.FromImage(pageBitmap))
                    {
                        using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.Red, 3)) // Red bounding box
                        {
                            foreach (var box in boundingBoxes)
                            {
                                if (box.PageId == i) // Only process matching page
                                {
                                    float scaleX = (float)(pageBitmap.Width / pdfWidth);
                                    float scaleY = (float)(pageBitmap.Height / pdfHeight);

                                    float xImage = (float)(box.X * scaleX);
                                    float widthImage = (float)(box.Width * scaleX);
                                    float yImage = (float)((pdfHeight - box.Y - box.Height) * scaleY);
                                    float heightImage = (float)(box.Height * scaleY);

                                    g.DrawRectangle(pen, xImage, yImage, widthImage, heightImage);
                                }
                            }
                        }
                    }

                    modifiedPages.Add(pageBitmap); // Store modified page
                }
                return CreateTADocument(modifiedPages, TASDKURL, TASession);
                //return SaveMultiPageTiff(modifiedPages); // Return final TIFF
            }
        }



        private byte[] GetKTADocumentFileAsTiff(string docID, string ktaSDKUrl, string sessionID)
        {
            byte[] result = new byte[1];
            byte[] buffer = new byte[4096];
            //string fileType = "pdf";
            string status = "OK";

            try
            {
                //Setting the URi and calling the get document API
                var KTAGetDocumentFile = ktaSDKUrl + "/CaptureDocumentService.svc/json/GetDocumentFile2";
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(KTAGetDocumentFile);
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";

                // CONSTRUCT JSON Payload
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    string json = "{\"sessionId\":\"" + sessionID + "\",\"reportingData\": {\"Station\": \"\", \"MarkCompleted\": false }, \"documentId\":\"" + docID + "\", \"documentFileOptions\": { \"FileType\": \".tiff\", \"IncludeAnnotations\": 0 }}";

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
            }
            catch (Exception ex)
            {
                status = "An error occured: " + ex.ToString();
                return result;
            }
        }



        private string CreateTADocument(List<Bitmap> pages, string ktaSDKUrl, string sessionID)
        {
            JObject result = null;
            string returnDocumentId = string.Empty;

            try
            {
                var createDocumentURL = ktaSDKUrl + "/CaptureDocumentService.svc/json/CreateDocument3";
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(createDocumentURL);
                request.Timeout = 300000;
                request.ContentType = "application/json";
                request.Method = "POST";

                // Prepare PageDataList dynamically for each Bitmap (image)
                var pageDataList = pages.Select(page => new
                {
                    Base64Data = Convert.ToBase64String(BitmapToByteArray(page, ImageFormat.Tiff)), // Convert each page to Base64
                    MimeType = "image/tiff", // MIME type for the page, can be dynamic if needed
                    Data = (object)null, // Data is null since we are using Base64Data
                    RuntimeFields = (object)null // Optional fields, can be added if needed
                }).ToArray();

                // Construct JSON payload
                var requestPayload = new
                {
                    sessionId = sessionID,
                    reportingData = (object)null,  // Optional
                    parentId = "",
                    runtimeFolderFields = (object)null,  // Optional
                    folderTypeId = "",
                    documentDataInput = new
                    {
                        PageDataList = pageDataList, // Populate with all pages
                        MimeType = "image/tiff"  // Overall MIME type for the document
                    },
                    insertIndex = 0
                };

                // Serialize the object to JSON
                string json = JsonConvert.SerializeObject(requestPayload);

                using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                {
                    streamWriter.Write(json);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    result = JObject.Parse(reader.ReadToEnd()); // Return the raw JSON response
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


        // Helper method to convert Bitmap to byte array
        private byte[] BitmapToByteArray(Bitmap bitmap, ImageFormat format)
        {
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, format);  // Save the bitmap as a stream in the specified format
                return memoryStream.ToArray();  // Return the byte array
            }
        }









    }
}
