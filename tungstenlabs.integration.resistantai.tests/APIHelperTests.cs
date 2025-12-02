using System.Collections.Generic;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tungstenlabs.integration.resistantai.tests
{
    [TestClass]
    public class APIHelperTests
    {
        private ResistantAIConnector oRAI = new ResistantAIConnector();
       private DocumentAnalysis DAHelper = new DocumentAnalysis();

        //[TestMethod]
        //public void UploadFileAndFetchResults()
        //{




        //    string temp;
        //    string suspendReason;
        //    //string[] response = oRAI.UploadFileAndFetchResults(
        //    //    Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET, "DocumentName.PDF",
        //    //    @"7ee246e1-b3be-4fd4-9303-b323013e19d3", //document id
        //    //    Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);

        //    string[] response1 = oRAI.UploadFileAndFetchResultsWithRetries(
        //        Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET, "DocumentName.PDF",
        //        @"7ee246e1-b3be-4fd4-9303-b323013e19d3", //document id
        //        Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID, 10, out suspendReason);

        //    string[] result = oRAI.GetAdaptiveResult(Constants.RAI_URL_API, response1[0],3, Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);
        //        }
        [TestMethod]        
        public void GetDocumentWithBoundingBoxes()
        {
            string temp;
            string docID = DAHelper.GetDocumentWithBoundingBoxes(Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                                                                    @"b6dfa9e3-48b5-4083-9d8c-b3a70170c123", Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                                                                    "91d115ee-30d7-42b8-999e-c4913964b3ef", "content_hiding");
        }
    }
}