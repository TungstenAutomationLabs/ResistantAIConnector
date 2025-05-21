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
                @"24a89003-fa7d-4239-9d88-b2a60145e0ab", //document id
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);
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