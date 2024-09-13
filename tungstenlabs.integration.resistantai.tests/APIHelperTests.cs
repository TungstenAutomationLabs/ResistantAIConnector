using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tungstenlabs.integration.resistantai.tests
{
    [TestClass]
    public class APIHelperTests
    {
        private ResistantAIConnector oRAI = new ResistantAIConnector();

        [TestMethod]
        public void UploadFileAndFetchResults()
        {
            string[] response = oRAI.UploadFileAndFetchResults(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                @"", //document id
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);
        }
    }
}