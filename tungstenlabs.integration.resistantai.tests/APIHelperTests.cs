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
            string[] response = oRAI.UploadFileAndFetchResults(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                @"09ef3632-b429-4dfb-b690-b30101053e67", //document id
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);


            string[] result = oRAI.GetAdaptiveResult(Constants.RAI_URL_API, response[0], Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);
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