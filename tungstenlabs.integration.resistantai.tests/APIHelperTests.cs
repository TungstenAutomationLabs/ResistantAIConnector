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
            string temp;
            string[] response = oRAI.UploadFileAndFetchResults(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                @"24a89003-fa7d-4239-9d88-b2a60145e0ab", //document id
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID, out temp);
        }
    }
}