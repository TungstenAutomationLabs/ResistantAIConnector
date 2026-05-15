using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace tungstenlabs.integration.resistantai.tests
{
    [TestClass]
    public class APIHelperTests
    {
        private ResistantAIConnector oRAI = new ResistantAIConnector();
        private DocumentAnalysis DAHelper = new DocumentAnalysis();

        private DO_ProxySettings dO_ProxySettings = new DO_ProxySettings()
        {
            Url = Constants.ProxyUrl,
            Username = Constants.ProxyUsername,
            Password = Constants.ProxyPassword
        };

        private string characteristicsJson = @"{
            ""identity_characteristics"": {
                ""first_name"": ""John"",
                ""last_name"": ""Doe"",
                ""email"": ""john.doe@example.com"",
                ""user_id"": ""USR-001""
            },
            ""customer_case_id"": ""CASE-2026-001"",
            ""customer_tenant_id"": ""TENANT-001""
        }";

        private const string DocId_NoProxy = @"f61fa7c8-97b0-4991-b9a9-b4420160a9b4";
        private const string DocId_WithProxy = @"f61fa7c8-97b0-4991-b9a9-b4420160a9b4";

        // Shared submission IDs across tests
        // Run tests in order 1-10 in a single session for IDs to flow correctly
        private static string _submissionId_NoProxy = "";
        private static string _submissionId_WithProxy = "";
        private static string _submissionId_Characteristics_NoProxy = "";
        private static string _submissionId_Characteristics_WithProxy = "";

        private bool IsSuccess(string json)
        {
            return json.Contains("\"status\": \"SUCCESS\"") || json.Contains("\"status\":\"SUCCESS\"");
        }

        // =============================================
        // 1. Standalone: UploadFileAndFetchResults (no retry, no proxy)
        // =============================================
        [TestMethod]
        public void Test_01_UploadFileAndFetchResults_NoRetry_NoProxy()
        {
            string[] response = oRAI.UploadFileAndFetchResults(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                "DocumentName.PDF", DocId_NoProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID);

            string submissionId = response[0];
            string fraudResult = response[1];

            Assert.IsFalse(string.IsNullOrEmpty(submissionId), "Submission ID should not be empty");
            Assert.IsTrue(IsSuccess(fraudResult), $"Fraud result: {fraudResult}");
        }

        // =============================================
        // 2. No Proxy - Fraud Result
        // Sets _submissionId_NoProxy for tests 07 and 09
        // =============================================
        [TestMethod]
        public void Test_02_NoProxy_FraudResult()
        {
            string suspendReason;

            string[] fraudResponse = oRAI.UploadFileAndFetchResultsWithRetries(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                "DocumentName.PDF", DocId_NoProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                10, out suspendReason);

            _submissionId_NoProxy = fraudResponse[0];
            string fraudResult = fraudResponse[1];

            Assert.IsTrue(string.IsNullOrEmpty(suspendReason), $"Suspend reason: {suspendReason}");
            Assert.IsFalse(string.IsNullOrEmpty(_submissionId_NoProxy), "Submission ID should not be empty");
            Assert.IsTrue(IsSuccess(fraudResult), $"Fraud result: {fraudResult}");
        }

        // =============================================
        // 3. With Proxy - Fraud Result
        // Sets _submissionId_WithProxy for tests 08 and 10
        // =============================================
        [TestMethod]
        public void Test_03_WithProxy_FraudResult()
        {
            string suspendReason;

            string[] fraudResponse = oRAI.UploadFileAndFetchResultsWithRetries1(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                "DocumentName.PDF", DocId_WithProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                10, dO_ProxySettings, out suspendReason);

            _submissionId_WithProxy = fraudResponse[0];
            string fraudResult = fraudResponse[1];

            Assert.IsTrue(string.IsNullOrEmpty(suspendReason), $"Suspend reason: {suspendReason}");
            Assert.IsFalse(string.IsNullOrEmpty(_submissionId_WithProxy), "Submission ID should not be empty");
            Assert.IsTrue(IsSuccess(fraudResult), $"Fraud result: {fraudResult}");
        }

        // =============================================
        // 4. Submission Characteristics - Flag Disabled (No Proxy)
        // Ensure RAI-ENABLE-SUBMISSION-CHARACTERISTICS = false before running
        // =============================================
        [TestMethod]
        public void Test_04_Characteristics_NoProxy_FlagDisabled()
        {
            string notes;

            string[] fraudResponse = oRAI.UploadFileAndFetchResultsWithCharacteristics(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                "DocumentName.PDF", DocId_NoProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                10, characteristicsJson, out notes);

            _submissionId_Characteristics_NoProxy = fraudResponse[0];
            string fraudResult = fraudResponse[1];

            Assert.IsTrue(notes.Contains("RAI-ENABLE-SUBMISSION-CHARACTERISTICS is false"),
                $"Expected warning in notes but got: {notes}");
            Assert.IsFalse(string.IsNullOrEmpty(_submissionId_Characteristics_NoProxy), "Submission ID should not be empty");
            Assert.IsTrue(IsSuccess(fraudResult), $"Fraud result: {fraudResult}");
        }

        // =============================================
        // 5. Submission Characteristics - Flag Enabled (No Proxy)
        // Ensure RAI-ENABLE-SUBMISSION-CHARACTERISTICS = true before running
        // Sets _submissionId_Characteristics_NoProxy
        // =============================================
        [TestMethod]
        public void Test_05_Characteristics_NoProxy_FlagEnabled()
        {
            string notes;

            string[] fraudResponse = oRAI.UploadFileAndFetchResultsWithCharacteristics(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                "DocumentName.PDF", DocId_NoProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                10, characteristicsJson, out notes);

            _submissionId_Characteristics_NoProxy = fraudResponse[0];
            string fraudResult = fraudResponse[1];

            Assert.IsTrue(notes.Contains("Submission Characteristics submitted successfully."),
                $"Expected success message in notes but got: {notes}");
            Assert.IsFalse(string.IsNullOrEmpty(_submissionId_Characteristics_NoProxy), "Submission ID should not be empty");
            Assert.IsTrue(IsSuccess(fraudResult), $"Fraud result: {fraudResult}");
        }

        // =============================================
        // 6. Submission Characteristics - Flag Enabled (With Proxy)
        // Ensure RAI-ENABLE-SUBMISSION-CHARACTERISTICS = true before running
        // Sets _submissionId_Characteristics_WithProxy
        // =============================================
        [TestMethod]
        public void Test_06_Characteristics_WithProxy_FlagEnabled()
        {
            string notes;

            string[] fraudResponse = oRAI.UploadFileAndFetchResultsWithCharacteristics1(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                "DocumentName.PDF", DocId_WithProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                10, characteristicsJson, dO_ProxySettings, out notes);

            _submissionId_Characteristics_WithProxy = fraudResponse[0];
            string fraudResult = fraudResponse[1];

            Assert.IsTrue(notes.Contains("Submission Characteristics submitted successfully."),
                $"Expected success message in notes but got: {notes}");
            Assert.IsFalse(string.IsNullOrEmpty(_submissionId_Characteristics_WithProxy), "Submission ID should not be empty");
            Assert.IsTrue(IsSuccess(fraudResult), $"Fraud result: {fraudResult}");
        }

        // =============================================
        // 7. No Proxy - Adaptive Result
        // Requires Test_02 to have run first in same session
        // =============================================
        [TestMethod]
        public void Test_07_NoProxy_AdaptiveResult()
        {
            if (string.IsNullOrEmpty(_submissionId_NoProxy))
                Assert.Inconclusive("Submission ID not available. Please run Test_02_NoProxy_FraudResult first in the same session.");

            string[] result = oRAI.GetAdaptiveResult(
                Constants.RAI_URL_API, _submissionId_NoProxy, 3,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                Constants.RAI_URL_TOKEN, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET);

            Assert.IsFalse(string.IsNullOrEmpty(result[0]), "Adaptive decision should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(result[1]), "Adaptive reason should not be empty");
        }

        // =============================================
        // 8. With Proxy - Adaptive Result
        // Requires Test_03 to have run first in same session
        // =============================================
        [TestMethod]
        public void Test_08_WithProxy_AdaptiveResult()
        {
            if (string.IsNullOrEmpty(_submissionId_WithProxy))
                Assert.Inconclusive("Submission ID not available. Please run Test_03_WithProxy_FraudResult first in the same session.");

            string[] result = oRAI.GetAdaptiveResult1(
                Constants.RAI_URL_API, _submissionId_WithProxy, 3,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                dO_ProxySettings,
                Constants.RAI_URL_TOKEN, Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET);

            Assert.IsFalse(string.IsNullOrEmpty(result[0]), "Adaptive decision should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(result[1]), "Adaptive reason should not be empty");
        }

        // =============================================
        // 9. No Proxy - Bounding Boxes
        // Requires Test_02 to have run first in same session
        // =============================================
        [TestMethod]
        public void Test_09_NoProxy_BoundingBoxes()
        {
            if (string.IsNullOrEmpty(_submissionId_NoProxy))
                Assert.Inconclusive("Submission ID not available. Please run Test_02_NoProxy_FraudResult first in the same session.");

            string boundingBoxNewDocId = DAHelper.GetDocumentWithBoundingBoxes(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                DocId_NoProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                _submissionId_NoProxy, "content_hiding");

            Assert.IsFalse(string.IsNullOrEmpty(boundingBoxNewDocId), "New bounding box document ID should not be empty");
        }

        // =============================================
        // 10. With Proxy - Bounding Boxes
        // Requires Test_03 to have run first in same session
        // =============================================
        [TestMethod]
        public void Test_10_WithProxy_BoundingBoxes()
        {
            if (string.IsNullOrEmpty(_submissionId_WithProxy))
                Assert.Inconclusive("Submission ID not available. Please run Test_03_WithProxy_FraudResult first in the same session.");

            string boundingBoxNewDocId = DAHelper.GetDocumentWithBoundingBoxes1(
                Constants.RAI_URL_TOKEN, Constants.RAI_URL_API,
                Constants.RAI_CLIENT_ID, Constants.RAI_CLIENT_SECRET,
                DocId_WithProxy,
                Constants.TOTALAGILITY_API_URL, Constants.TOTALAGILITY_SESSION_ID,
                _submissionId_WithProxy, "content_hiding", dO_ProxySettings);

            Assert.IsFalse(string.IsNullOrEmpty(boundingBoxNewDocId), "New bounding box document ID should not be empty");
        }
    }
}