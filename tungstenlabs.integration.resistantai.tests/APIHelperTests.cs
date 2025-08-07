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

        [TestMethod]
        public void UploadFileAndFetchResults()
        {




            string temp;
            string suspendReason;
            string[] response = oRAI.UploadFileAndFetchResults(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET, "DocumentName.PDF",
                @"7ee246e1-b3be-4fd4-9303-b323013e19d3", //document id
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);

            string[] response1 = oRAI.UploadFileAndFetchResultsWithRetries(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET, "DocumentName.PDF",
                @"7ee246e1-b3be-4fd4-9303-b323013e19d3", //document id
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID, 10, out suspendReason);

            string[] result = oRAI.GetAdaptiveResult(Constants.RAI_URL_API, response[0],3, Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);
                }
        [TestMethod]        
        public void GetDocumentWithBoundingBoxes()
        {
            string temp;
            string docID = DAHelper.GetDocumentWithBoundingBoxes(Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                                                                    @"d943bd2f-e2ce-463f-9715-b29901128ae8", Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID, 
                                                                    "ad90a052-b7d4-4897-83d7-f01e281728cc", "content_hiding");
        }
    }
}