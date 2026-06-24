using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace tungstenlabs.integration.raidiagnostics
{
    /// <summary>
    /// Result of a single diagnostic validation step.
    /// </summary>
    public class DiagnosticResult
    {
        public string StepName { get; set; }
        public string Status { get; set; }             // "Pass", "Fail", "Skipped"
        public bool Passed { get; set; }
        public string ErrorMessage { get; set; }
        public string SuggestedArea { get; set; }      // "Network/Firewall", "TotalAgility Configuration", "Authentication", "Resistant AI Service"
        public string Details { get; set; }            // safe technical details (no secrets)
    }

    /// <summary>
    /// Standalone diagnostic entry point for the RAI Connector.
    ///
    /// NOT called as part of the standard document processing workflow.
    /// Intended to be invoked manually (e.g. via a dedicated TA Business Process / .NET activity)
    /// during initial setup or troubleshooting.
    ///
    /// Single entry point: RunDiagnostics.
    /// If RAI-PROXY-ENABLE is set to true in TotalAgility Server Variables, proxy settings are
    /// read automatically from RAI-PROXY-URL, RAI-PROXY-USERNAME, and RAI-PROXY-PASSWORD.
    /// </summary>
    public class DiagnosticHelper
    {
        // =========================================================
        // Server variable name constants (defined locally — no dependency on resistantai assembly)
        // =========================================================
        private const string RAI_URL_TOKEN = "RAI-URL-TOKEN";
        private const string RAI_URL_API = "RAI-URL-API";
        private const string RAI_CLIENT_ID = "RAI-CLIENT-ID";
        private const string RAI_CLIENT_SECRET = "RAI-CLIENT-SECRET";
        private const string RAI_CLIENT_TOKEN = "RAI-CLIENT-TOKEN";
        private const string RAI_ENABLE_DECISION = "RAI-ENABLE-DECISION";
        private const string RAI_ENABLE_SUBMISSION_CHARACTERISTICS = "RAI-ENABLE-SUBMISSION-CHARACTERISTICS";
        private const string RAI_PROXY_ENABLE = "RAI-PROXY-ENABLE";
        private const string RAI_PROXY_URL = "RAI-PROXY-URL";
        private const string RAI_PROXY_USERNAME = "RAI-PROXY-USERNAME";
        private const string RAI_PROXY_PASSWORD = "RAI-PROXY-PASSWORD";
        private const string TOTALAGILITY_URL_API = "TOTALAGILITY-URL-API";

        private IWebProxy _cachedProxy = null;

        // =========================================================
        // PUBLIC ENTRY POINT
        // =========================================================

        /// <summary>
        /// Runs the full diagnostic suite.
        /// All RAI credentials and URLs are read from TotalAgility Server Variables automatically.
        ///
        /// IMPORTANT: Before running diagnostics, ensure the following server variables are NOT
        /// marked Secure in TotalAgility: RAI-URL-TOKEN, RAI-URL-API, RAI-CLIENT-ID,
        /// RAI-CLIENT-SECRET, RAI-CLIENT-TOKEN. Once diagnostics are complete, re-secure them.
        ///
        /// Proxy configuration is also read automatically — if RAI-PROXY-ENABLE is true,
        /// RAI-PROXY-URL / RAI-PROXY-USERNAME / RAI-PROXY-PASSWORD are used for all outbound RAI traffic.
        /// </summary>
        /// <param name="TASDKURL">TotalAgility SDK URL</param>
        /// <param name="TASession">TotalAgility session ID</param>
        /// <returns>JSON array of DiagnosticResult, suitable for assigning to a TA string output parameter.</returns>
        public string RunDiagnostics(string TASDKURL, string TASession)
        {
            // Read all required credentials from server variables upfront.
            // Fails fast with a clear message if any variable is missing or still marked Secure.
            string authenticationURL, submissionURL, clientID, clientSecret;
            if (!TryReadCredentials(TASDKURL, TASession, out authenticationURL, out submissionURL, out clientID, out clientSecret, out string credentialError))
            {
                var earlyExit = new List<DiagnosticResult>
                {
                    new DiagnosticResult
                    {
                        StepName = "Server Variables — Pre-flight Check",
                        Status = "Fail",
                        Passed = false,
                        ErrorMessage = credentialError,
                        SuggestedArea = "TotalAgility Configuration",
                        Details = "Ensure RAI-URL-TOKEN, RAI-URL-API, RAI-CLIENT-ID, RAI-CLIENT-SECRET and RAI-CLIENT-TOKEN " +
                                  "are all present and NOT marked Secure before running diagnostics. Re-secure them afterwards."
                    }
                };
                return JsonConvert.SerializeObject(earlyExit, Formatting.Indented);
            }

            _cachedProxy = ResolveProxy(TASDKURL, TASession, out string proxyError);
            return ExecuteDiagnostics(authenticationURL, submissionURL, clientID, clientSecret, TASDKURL, TASession, proxyError);
        }

        /// <summary>
        /// Attempts to read all required RAI credentials from TA Server Variables.
        /// Returns false with a clear error message if any variable is missing or marked Secure.
        /// </summary>
        private bool TryReadCredentials(string TASDKURL, string TASession,
            out string authenticationURL, out string submissionURL, out string clientID, out string clientSecret,
            out string error)
        {
            authenticationURL = submissionURL = clientID = clientSecret = error = null;

            string[] required = new[]
            {
                RAI_URL_TOKEN,
                RAI_URL_API,
                RAI_CLIENT_ID,
                RAI_CLIENT_SECRET,
                RAI_CLIENT_TOKEN
            };

            try
            {
                var vars = GetServerVariables(TASession, TASDKURL, new List<string>(required));
                authenticationURL = vars[RAI_URL_TOKEN];
                submissionURL = vars[RAI_URL_API];
                clientID = vars[RAI_CLIENT_ID];
                clientSecret = vars[RAI_CLIENT_SECRET];
                return true;
            }
            catch (Exception ex) when (ex.Message.Contains("secure"))
            {
                error = $"One or more required server variables are marked Secure and cannot be read. " +
                        $"Please uncheck Secure on RAI-URL-TOKEN, RAI-URL-API, RAI-CLIENT-ID, RAI-CLIENT-SECRET " +
                        $"and RAI-CLIENT-TOKEN before running diagnostics, then re-secure them afterwards. Detail: {ex.Message}";
                return false;
            }
            catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("was not found"))
            {
                error = $"One or more required server variables are missing from TotalAgility. " +
                        $"Ensure all of the following are configured: RAI-URL-TOKEN, RAI-URL-API, RAI-CLIENT-ID, " +
                        $"RAI-CLIENT-SECRET, RAI-CLIENT-TOKEN. Detail: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Unable to read server variables from TotalAgility. Detail: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Reads and validates proxy configuration from TA Server Variables.
        /// Returns the proxy if valid, null if disabled.
        /// Sets proxyError if proxy is enabled but misconfigured.
        /// </summary>
        private IWebProxy ResolveProxy(string TASDKURL, string TASession, out string proxyError)
        {
            proxyError = null;

            try
            {
                // Read only the enable flag first — never proxied, always internal
                var enableVars = GetServerVariables(TASession, TASDKURL, new List<string> { RAI_PROXY_ENABLE });

                bool enabled = enableVars.TryGetValue(RAI_PROXY_ENABLE, out string enableVal) &&
                               enableVal.ToLower() == "true";

                if (!enabled)
                    return null;

                // Only read remaining proxy vars if proxy is actually enabled
                var proxyVars = GetServerVariables(TASession, TASDKURL,
                    new List<string> { RAI_PROXY_URL, RAI_PROXY_USERNAME, RAI_PROXY_PASSWORD });

                string proxyUrl = proxyVars.TryGetValue(RAI_PROXY_URL, out string u) ? u : null;
                string username = proxyVars.TryGetValue(RAI_PROXY_USERNAME, out string un) ? un : null;
                string password = proxyVars.TryGetValue(RAI_PROXY_PASSWORD, out string pw) ? pw : null;

                // Validate
                if (string.IsNullOrWhiteSpace(proxyUrl))
                {
                    proxyError = "RAI-PROXY-ENABLE is true but RAI-PROXY-URL is empty. Provide a valid proxy URL (e.g. http://proxy.company.com:8080).";
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
                {
                    proxyError = "RAI-PROXY-USERNAME is set but RAI-PROXY-PASSWORD is empty. Provide a password or clear the username to use Windows authentication.";
                    return null;
                }

                WebProxy proxy = new WebProxy(proxyUrl)
                {
                    BypassProxyOnLocal = false,
                    BypassList = Array.Empty<string>()
                };

                if (!string.IsNullOrWhiteSpace(username))
                    proxy.Credentials = new NetworkCredential(username, password);
                else
                    proxy.UseDefaultCredentials = true;

                return proxy;
            }
            catch
            {
                // Proxy vars not configured — proceed without proxy
                return null;
            }
        }

        // =========================================================
        // CORE ORCHESTRATION
        // =========================================================

        private string ExecuteDiagnostics(string AuthenticationURL, string SubmissionURL, string ClientID, string ClientSecret,
                                           string TASDKURL, string TASession, string proxyError)
        {
            List<DiagnosticResult> results = new List<DiagnosticResult>();

            // ---------------------------------------------------------
            // Step 0: Proxy configuration validation (only when proxy is enabled)
            // ---------------------------------------------------------
            if (!string.IsNullOrEmpty(proxyError))
            {
                results.Add(new DiagnosticResult
                {
                    StepName = "Proxy Configuration",
                    Status = "Fail",
                    Passed = false,
                    ErrorMessage = proxyError,
                    SuggestedArea = "TotalAgility Configuration",
                    Details = "Fix the proxy configuration in TotalAgility Server Variables before re-running diagnostics."
                });
                return JsonConvert.SerializeObject(results, Formatting.Indented);
            }

            if (_cachedProxy != null)
            {
                results.Add(new DiagnosticResult
                {
                    StepName = "Proxy Configuration",
                    Status = "Pass",
                    Passed = true,
                    Details = "RAI-PROXY-ENABLE is true. Proxy settings are valid and will be used for all outbound RAI traffic."
                });
            }

            // ---------------------------------------------------------
            // Step 1: TotalAgility SDK reachability
            // ---------------------------------------------------------
            bool taSdkReachable = CheckTASdkReachability(TASDKURL, TASession, results);

            // ---------------------------------------------------------
            // Step 2: Required TA Server Variables present
            // ---------------------------------------------------------
            if (taSdkReachable)
            {
                CheckRequiredServerVariables(TASDKURL, TASession, results);
                CheckOptionalServerVariables(TASDKURL, TASession, results);
            }
            else
            {
                results.Add(Skipped("TotalAgility Server Variables — Required", "Skipped because TotalAgility SDK was not reachable.", "TotalAgility Configuration"));
                results.Add(Skipped("TotalAgility Server Variables — Optional Feature Flags", "Skipped because TotalAgility SDK was not reachable.", "TotalAgility Configuration"));
            }

            // ---------------------------------------------------------
            // Step 3: RAI authentication endpoint — network reachability
            // ---------------------------------------------------------
            bool tokenEndpointReachable = CheckTokenEndpointReachability(AuthenticationURL, results);

            // ---------------------------------------------------------
            // Step 4: RAI submission endpoint — network reachability (independent of auth)
            // ---------------------------------------------------------
            CheckSubmissionEndpointReachability(SubmissionURL, results);

            // ---------------------------------------------------------
            // Step 5: RAI authentication (Client ID / Secret)
            // ---------------------------------------------------------
            string accessToken = null;
            if (tokenEndpointReachable)
            {
                accessToken = CheckAuthentication(AuthenticationURL, ClientID, ClientSecret, results);
            }
            else
            {
                results.Add(Skipped("Resistant AI Authentication", "Skipped because the authentication endpoint was not reachable.", "Network/Firewall"));
            }

            // ---------------------------------------------------------
            // Step 6: RAI submission creation (proves token + submission API together)
            // ---------------------------------------------------------
            string submissionId = null;
            if (!string.IsNullOrEmpty(accessToken))
            {
                submissionId = CheckSubmissionCreation(SubmissionURL, accessToken, results);
            }
            else
            {
                results.Add(Skipped("Resistant AI Submission Creation", "Skipped because authentication did not succeed.", "Authentication"));
            }

            // ---------------------------------------------------------
            // Step 7: In-memory document upload — proves S3 whitelisting
            // ---------------------------------------------------------
            string diagnosticSubmissionId = null;
            if (!string.IsNullOrEmpty(accessToken))
            {
                diagnosticSubmissionId = CheckDocumentUpload(SubmissionURL, accessToken, results);
            }
            else
            {
                results.Add(Skipped("Document Upload (S3)", "Skipped because authentication did not succeed.", "N/A"));
            }

            // ---------------------------------------------------------
            // Step 8: Fraud result retrieval — proves full end-to-end path
            // ---------------------------------------------------------
            if (!string.IsNullOrEmpty(diagnosticSubmissionId))
            {
                CheckFraudResult(SubmissionURL, accessToken, diagnosticSubmissionId, results);
            }
            else
            {
                results.Add(Skipped("Fraud Result Retrieval", "Skipped because document upload did not succeed.", "N/A"));
            }

            return JsonConvert.SerializeObject(results, Formatting.Indented);
        }

        // =========================================================
        // STEP IMPLEMENTATIONS
        // =========================================================

        /// <summary>Step 1 — confirms the TA SDK endpoint is reachable and responds.</summary>
        private bool CheckTASdkReachability(string TASDKURL, string TASession, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "TotalAgility SDK Reachability" };

            try
            {
                if (string.IsNullOrWhiteSpace(TASDKURL))
                {
                    result.Passed = false;
                    result.ErrorMessage = "TASDKURL was not provided.";
                    result.SuggestedArea = "TotalAgility Configuration";
                    Finish(results, result);
                    return false;
                }

                string url = $"{TASDKURL}/ServerService.svc/json/GetServerVariables";
                var payload = new
                {
                    sessionId = TASession,
                    serverVariablesFilter = new
                    {
                        CategoryIdentity = new { Id = "", Name = "" },
                        ServerIdentity = new { Id = "", Name = "" },
                        SearchText = "RAI-URL-TOKEN"
                    }
                };

                using (HttpClient client = CreateHttpClient(timeoutSeconds: 15, useProxy: false))
                {
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = client.PostAsync(url, content).GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode)
                    {
                        result.Passed = true;
                        result.Details = $"Reached {TASDKURL}/ServerService.svc successfully (HTTP {(int)response.StatusCode}).";

                        // Also verify input TASDKURL matches TOTALAGILITY-URL-API server variable
                        try
                        {
                            var svVars = GetServerVariables(TASession, TASDKURL, new List<string> { TOTALAGILITY_URL_API });
                            string svValue = svVars.TryGetValue(TOTALAGILITY_URL_API, out string sv) ? sv?.TrimEnd('/') : null;
                            string inputValue = TASDKURL.TrimEnd('/');

                            if (string.IsNullOrWhiteSpace(svValue))
                            {
                                result.Passed = false;
                                result.ErrorMessage = $"TOTALAGILITY-URL-API server variable is empty. Set it to: {TASDKURL}.";
                                result.SuggestedArea = "TotalAgility Configuration";
                            }
                            else if (!string.Equals(svValue, inputValue, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Passed = false;
                                result.ErrorMessage = $"TOTALAGILITY-URL-API server variable value '{svValue}' does not match the input TASDKURL '{inputValue}'. Update the server variable to match.";
                                result.SuggestedArea = "TotalAgility Configuration";
                            }
                            else
                            {
                                result.Details += $" TOTALAGILITY-URL-API server variable matches input.";
                            }
                        }
                        catch (Exception svEx)
                        {
                            // TOTALAGILITY-URL-API not configured — warn but don't fail reachability
                            result.Details += $" Warning: could not verify TOTALAGILITY-URL-API server variable ({svEx.Message}).";
                        }
                    }
                    else
                    {
                        result.Passed = false;
                        result.ErrorMessage = $"TA SDK responded with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
                        result.SuggestedArea = "TotalAgility Configuration";
                    }
                }
            }
            catch (HttpRequestException ex) when (IsCertificateError(ex))
            {
                result.Passed = false;
                result.ErrorMessage = "A certificate validation error occurred connecting to the TotalAgility SDK URL. This commonly indicates an SSL certificate trust issue (e.g. Load Balancer).";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "Unable to reach the TotalAgility SDK URL. Check that the URL is correct, the service is running, and network routing/certificates between this server and the SDK endpoint are valid.";
                result.SuggestedArea = "TotalAgility Configuration / Network";
                result.Details = ex.Message;
            }

            Finish(results, result);
            return result.Passed;
        }

        /// <summary>Step 2a — confirms all required server variables are present and non-empty.
        /// Readable variables (RAI-URL-TOKEN, RAI-URL-API, RAI-CLIENT-ID) are fetched together.
        /// Secure variables (RAI-CLIENT-SECRET, RAI-CLIENT-TOKEN) are probed individually —
        /// ServerVariableHelper throws on secure vars, so a throw means "present and secured" (correct),
        /// while a "not found" exception means it is missing entirely.
        /// </summary>
        private void CheckRequiredServerVariables(string TASDKURL, string TASession, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "TotalAgility Server Variables — Required" };

            // Readable (non-secure) variables
            string[] readableVars = new[]
            {
                RAI_URL_TOKEN,
                RAI_URL_API,
                RAI_CLIENT_ID
            };

            // Secure variables — must be probed one at a time
            string[] secureVars = new[]
            {
                RAI_CLIENT_SECRET,
                RAI_CLIENT_TOKEN
            };

            List<string> missing = new List<string>();
            List<string> present = new List<string>();
            List<string> securePresent = new List<string>();

            try
            {
                // 1. Fetch readable variables as a batch
                var vars = GetServerVariables(TASession, TASDKURL, new List<string>(readableVars));

                foreach (var varName in readableVars)
                {
                    if (!vars.ContainsKey(varName) || string.IsNullOrWhiteSpace(vars[varName]))
                        missing.Add(varName);
                    else
                        present.Add(varName);
                }

                // 2. Probe each secure variable individually
                foreach (var varName in secureVars)
                {
                    try
                    {
                        GetServerVariables(TASession, TASDKURL, new List<string> { varName });
                        // If we get here the variable exists but is NOT marked secure — still counts as present
                        present.Add(varName);
                    }
                    catch (Exception ex) when (ex.Message.Contains("secure"))
                    {
                        // Correct: variable exists and is properly secured
                        securePresent.Add(varName);
                    }
                    catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("was not found"))
                    {
                        // Variable does not exist at all
                        missing.Add(varName);
                    }
                    catch
                    {
                        // Any other error on a secure var probe — treat as missing to be safe
                        missing.Add(varName);
                    }
                }

                if (missing.Count == 0)
                {
                    result.Passed = true;
                    result.Details = $"Readable: {string.Join(", ", present)}." +
                                     (securePresent.Count > 0 ? $" Secured: {string.Join(", ", securePresent)}." : "");
                }
                else
                {
                    result.Passed = false;
                    result.ErrorMessage = $"The following required server variables are missing or empty: {string.Join(", ", missing)}.";
                    result.SuggestedArea = "TotalAgility Configuration";
                    result.Details = $"Present (readable): {string.Join(", ", present)}. " +
                                     $"Present (secured): {string.Join(", ", securePresent)}.";
                }
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "Unable to retrieve server variables for validation.";
                result.SuggestedArea = "TotalAgility Configuration";
                result.Details = ex.Message;
            }

            Finish(results, result);
        }

        /// <summary>Step 2b — reports on optional feature-flag variables (informational, never fails the suite).</summary>
        private void CheckOptionalServerVariables(string TASDKURL, string TASession, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "TotalAgility Server Variables — Optional Feature Flags" };

            string[] optionalVars = new[]
            {
                RAI_ENABLE_DECISION,
                RAI_ENABLE_SUBMISSION_CHARACTERISTICS
            };

            try
            {
                var vars = GetServerVariables(TASession, TASDKURL, new List<string>(optionalVars));

                List<string> notes = new List<string>();
                foreach (var varName in optionalVars)
                {
                    if (vars.ContainsKey(varName))
                        notes.Add($"{varName} = '{vars[varName]}'");
                    else
                        notes.Add($"{varName} — not found (defaults to false / disabled)");
                }

                result.Passed = true;
                result.Details = string.Join("; ", notes);
            }
            catch (Exception ex)
            {
                // Optional vars — don't fail, just note the situation.
                result.Passed = true;
                result.Details = $"Optional feature-flag variables could not be read (may not be configured yet, which is fine). Details: {ex.Message}";
            }

            Finish(results, result);
        }

        /// <summary>Step 3 — confirms the RAI token endpoint is reachable over the network.</summary>
        private bool CheckTokenEndpointReachability(string AuthenticationURL, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "Resistant AI Authentication Endpoint — Network Reachability" };

            try
            {
                if (string.IsNullOrWhiteSpace(AuthenticationURL))
                {
                    result.Passed = false;
                    result.ErrorMessage = "AuthenticationURL (RAI-URL-TOKEN) was not provided.";
                    result.SuggestedArea = "TotalAgility Configuration";
                    Finish(results, result);
                    return false;
                }

                using (HttpClient client = CreateHttpClient(timeoutSeconds: 15))
                {
                    // A bare GET to an Okta token endpoint returns 4xx — that still proves network reachability.
                    var response = client.GetAsync(AuthenticationURL).GetAwaiter().GetResult();
                    result.Passed = true;
                    result.Details = $"Endpoint responded with HTTP {(int)response.StatusCode}. Network path is open.";
                }
            }
            catch (HttpRequestException ex) when (IsCertificateError(ex))
            {
                result.Passed = false;
                result.ErrorMessage = "A certificate validation error occurred connecting to the Resistant AI authentication endpoint.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "Unable to reach the Resistant AI authentication endpoint. This typically indicates a firewall or whitelisting issue. Ensure the Okta/RAI auth domain is whitelisted on TCP 443.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }

            Finish(results, result);
            return result.Passed;
        }

        /// <summary>Step 4 — confirms the RAI submission endpoint is reachable over the network (independent of auth).</summary>
        private void CheckSubmissionEndpointReachability(string SubmissionURL, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "Resistant AI Submission Endpoint — Network Reachability" };

            try
            {
                if (string.IsNullOrWhiteSpace(SubmissionURL))
                {
                    result.Passed = false;
                    result.ErrorMessage = "SubmissionURL (RAI-URL-API) was not provided.";
                    result.SuggestedArea = "TotalAgility Configuration";
                    Finish(results, result);
                    return;
                }

                using (HttpClient client = CreateHttpClient(timeoutSeconds: 15))
                {
                    // A GET without a bearer token is expected to return 401/403 — still proves reachability.
                    var response = client.GetAsync(SubmissionURL).GetAwaiter().GetResult();
                    result.Passed = true;
                    result.Details = $"Endpoint responded with HTTP {(int)response.StatusCode}. Network path is open.";
                }
            }
            catch (HttpRequestException ex) when (IsCertificateError(ex))
            {
                result.Passed = false;
                result.ErrorMessage = "A certificate validation error occurred connecting to the Resistant AI submission endpoint.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "Unable to reach the Resistant AI submission endpoint. Ensure api.resistant.ai (or your regional variant) is whitelisted on TCP 443.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }

            Finish(results, result);
        }

        /// <summary>Step 5 — authenticates using Client ID + Secret and obtains a bearer token.</summary>
        private string CheckAuthentication(string AuthenticationURL, string ClientID, string ClientSecret, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "Resistant AI Authentication" };
            string accessToken = null;

            try
            {
                if (string.IsNullOrWhiteSpace(ClientID) || string.IsNullOrWhiteSpace(ClientSecret))
                {
                    result.Passed = false;
                    result.ErrorMessage = "ClientID or ClientSecret was not provided.";
                    result.SuggestedArea = "TotalAgility Configuration";
                    Finish(results, result);
                    return null;
                }

                string credentials = $"{ClientID}:{ClientSecret}";
                string base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));

                using (HttpClient client = CreateHttpClient(timeoutSeconds: 15))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);

                    var bodyContent = new StringContent(
                        "grant_type=client_credentials&scope=submissions.read submissions.write",
                        Encoding.UTF8, "application/x-www-form-urlencoded");

                    var response = client.PostAsync(AuthenticationURL, bodyContent).GetAwaiter().GetResult();
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        result.Passed = false;
                        result.ErrorMessage = $"Authentication failed with HTTP {(int)response.StatusCode}. The Client ID and/or Client Secret were rejected by Resistant AI. Verify RAI-CLIENT-ID and RAI-CLIENT-SECRET in TotalAgility Server Variables.";
                        result.SuggestedArea = "Authentication";
                    }
                    else if (response.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(body);
                        accessToken = json["access_token"]?.ToString();

                        if (string.IsNullOrEmpty(accessToken))
                        {
                            result.Passed = false;
                            result.ErrorMessage = "Authentication call succeeded but no access_token was returned in the response.";
                            result.SuggestedArea = "Resistant AI Service";
                        }
                        else
                        {
                            result.Passed = true;
                            // Only reveal the last 4 characters to aid correlation without exposing the token.
                            result.Details = $"Bearer token obtained successfully (last 4 chars: ...{accessToken.Substring(Math.Max(0, accessToken.Length - 4))}).";
                        }
                    }
                    else
                    {
                        result.Passed = false;
                        result.ErrorMessage = $"Unexpected response from authentication endpoint: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
                        result.SuggestedArea = "Resistant AI Service";
                    }
                }
            }
            catch (HttpRequestException ex) when (IsCertificateError(ex))
            {
                result.Passed = false;
                result.ErrorMessage = "A certificate validation error occurred while authenticating with Resistant AI.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "An unexpected error occurred while authenticating with Resistant AI.";
                result.SuggestedArea = "Authentication";
                result.Details = ex.Message;
            }

            Finish(results, result);
            return accessToken;
        }

        /// <summary>Step 6 — creates a minimal test submission to confirm the token and submission API work together.</summary>
        private string CheckSubmissionCreation(string SubmissionURL, string accessToken, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "Resistant AI Submission Creation" };
            string submissionId = null;

            try
            {
                if (string.IsNullOrWhiteSpace(SubmissionURL))
                {
                    result.Passed = false;
                    result.ErrorMessage = "SubmissionURL (RAI-URL-API) was not provided.";
                    result.SuggestedArea = "TotalAgility Configuration";
                    Finish(results, result);
                    return null;
                }

                using (HttpClient client = CreateHttpClient(timeoutSeconds: 30))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    string requestBody = @"{
                        ""query_id"": ""DIAGNOSTIC-TEST"",
                        ""pipeline_configuration"": ""FRAUD_ONLY"",
                        ""enable_decision"": false,
                        ""enable_submission_characteristics"": false
                    }";

                    var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    var response = client.PostAsync(SubmissionURL, content).GetAwaiter().GetResult();
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        result.Passed = false;
                        result.ErrorMessage = $"Submission creation failed with HTTP {(int)response.StatusCode}. The bearer token was rejected — possible token scope or permissions issue.";
                        result.SuggestedArea = "Authentication";
                    }
                    else if (response.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(body);
                        submissionId = json["submission_id"]?.ToString();
                        string uploadUrl = json["upload_url"]?.ToString();

                        if (string.IsNullOrEmpty(submissionId) || string.IsNullOrEmpty(uploadUrl))
                        {
                            result.Passed = false;
                            result.ErrorMessage = "Submission created but the response did not contain the expected submission_id and/or upload_url fields.";
                            result.SuggestedArea = "Resistant AI Service";
                        }
                        else
                        {
                            result.Passed = true;
                            result.Details = $"Test submission created successfully. SubmissionId: {submissionId}.";
                        }
                    }
                    else
                    {
                        result.Passed = false;
                        result.ErrorMessage = $"Unexpected response from submission endpoint: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
                        result.SuggestedArea = "Resistant AI Service";
                    }
                }
            }
            catch (HttpRequestException ex) when (IsCertificateError(ex))
            {
                result.Passed = false;
                result.ErrorMessage = "A certificate validation error occurred connecting to the Resistant AI submission endpoint.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "An unexpected error occurred while creating a test submission.";
                result.SuggestedArea = "Resistant AI Service / Network";
                result.Details = ex.Message;
            }

            Finish(results, result);
            return submissionId;
        }

        /// <summary>
        /// Step 7 — generates a minimal valid PDF in memory, creates a new submission and uploads
        /// it to RAI via the S3 pre-signed URL. Proves S3 whitelisting without needing a TA document.
        /// Returns the submissionId for use in Step 8, or null on failure.
        /// </summary>
        private string CheckDocumentUpload(string SubmissionURL, string accessToken, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "Document Upload (S3)" };
            string submissionId = null;
            string uploadUrl = null;

            try
            {
                // 1. Create a submission for the test document
                using (HttpClient client = CreateHttpClient(timeoutSeconds: 30))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    string requestBody = @"{
                        ""query_id"": ""DIAGNOSTIC-DOC-TEST"",
                        ""pipeline_configuration"": ""FRAUD_ONLY"",
                        ""enable_decision"": false,
                        ""enable_submission_characteristics"": false
                    }";

                    var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    var response = client.PostAsync(SubmissionURL, content).GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        result.Passed = false;
                        result.ErrorMessage = $"Failed to create submission for test document. HTTP {(int)response.StatusCode}.";
                        result.SuggestedArea = "Resistant AI Service";
                        Finish(results, result);
                        return null;
                    }

                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var json = JObject.Parse(body);
                    submissionId = json["submission_id"]?.ToString();
                    uploadUrl = json["upload_url"]?.ToString();
                }

                // 2. Generate a minimal valid PDF in memory
                byte[] pdfBytes = GenerateMinimalPdf();

                // 3. Upload to S3 pre-signed URL
                using (HttpClient client = CreateHttpClient(timeoutSeconds: 60))
                {
                    var byteContent = new ByteArrayContent(pdfBytes);
                    byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    var response = client.PutAsync(uploadUrl, byteContent).GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        result.Passed = false;
                        result.ErrorMessage = $"Failed to upload test document to Resistant AI (S3). HTTP {(int)response.StatusCode}. This commonly indicates S3 endpoint whitelisting is missing (*.s3.amazonaws.com on TCP 443).";
                        result.SuggestedArea = "Network/Firewall (S3 endpoint whitelisting)";
                        Finish(results, result);
                        return null;
                    }
                }

                result.Passed = true;
                result.Details = $"Minimal test PDF ({pdfBytes.Length} bytes) uploaded successfully to Resistant AI. SubmissionId: {submissionId}.";
            }
            catch (HttpRequestException ex) when (IsCertificateError(ex))
            {
                result.Passed = false;
                result.ErrorMessage = "A certificate validation error occurred during the document upload.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
                submissionId = null;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "An unexpected error occurred during the document upload.";
                result.SuggestedArea = "Unknown — see Details";
                result.Details = ex.Message;
                submissionId = null;
            }

            Finish(results, result);
            return result.Passed ? submissionId : null;
        }

        /// <summary>
        /// Step 8 — polls for the fraud analysis result of the submission created in Step 7.
        /// Proves the full end-to-end path including RAI processing and result retrieval.
        /// </summary>
        private void CheckFraudResult(string SubmissionURL, string accessToken, string submissionId, List<DiagnosticResult> results)
        {
            var result = new DiagnosticResult { StepName = "Fraud Result Retrieval" };

            try
            {
                string requestUri = $"{SubmissionURL}/{submissionId}/fraud?with_metadata=false";
                int maxAttempts = 10;
                int delayMs = 4000;
                string resultJson = null;

                using (HttpClient client = CreateHttpClient(timeoutSeconds: 60))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    for (int attempt = 0; attempt <= maxAttempts; attempt++)
                    {
                        System.Threading.Thread.Sleep(delayMs);

                        var response = client.GetAsync(requestUri).GetAwaiter().GetResult();

                        if (response.IsSuccessStatusCode)
                        {
                            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            var json = JObject.Parse(body);
                            string status = json["status"]?.ToString();

                            // RAI returns status = "SUCCESS" or "PENDING" while processing
                            if (!string.IsNullOrEmpty(status) && status != "PENDING")
                            {
                                resultJson = body;
                                string score = json["score"]?.ToString();
                                result.Passed = true;
                                result.Details = $"Fraud result received. Status: {status}. Trust Score: {score ?? "N/A"}. SubmissionId: {submissionId}.";
                                break;
                            }
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                 response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            result.Passed = false;
                            result.ErrorMessage = $"Token rejected while polling for fraud result. HTTP {(int)response.StatusCode}.";
                            result.SuggestedArea = "Authentication";
                            Finish(results, result);
                            return;
                        }

                        delayMs += 1000 * attempt;
                    }

                    if (!result.Passed && string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        result.Passed = false;
                        result.ErrorMessage = "Fraud result was not available after maximum polling attempts. RAI may still be processing.";
                        result.SuggestedArea = "Resistant AI Service";
                    }
                }
            }
            catch (HttpRequestException ex) when (IsCertificateError(ex))
            {
                result.Passed = false;
                result.ErrorMessage = "A certificate validation error occurred while retrieving the fraud result.";
                result.SuggestedArea = "Network/Firewall";
                result.Details = ex.Message;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = "An unexpected error occurred while retrieving the fraud result.";
                result.SuggestedArea = "Resistant AI Service";
                result.Details = ex.Message;
            }

            Finish(results, result);
        }

        /// <summary>
        /// Generates a minimal but valid single-page PDF in memory.
        /// Small enough to process quickly, valid enough for RAI to analyse.
        /// </summary>
        private byte[] GenerateMinimalPdf()
        {
            // Minimal valid PDF structure — single blank page
            string pdf =
                "%PDF-1.4\n" +
                "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
                "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
                "3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Parent 2 0 R/Resources<<>>>>endobj\n" +
                "xref\n0 4\n" +
                "0000000000 65535 f\n" +
                "0000000009 00000 n\n" +
                "0000000058 00000 n\n" +
                "0000000115 00000 n\n" +
                "trailer<</Size 4/Root 1 0 R>>\n" +
                "startxref\n210\n%%EOF";

            return Encoding.ASCII.GetBytes(pdf);
        }

        // =========================================================
        // HELPERS
        // =========================================================

        /// <summary>
        /// Creates an HttpClient that respects the cached proxy (if any).
        /// When useProxy is explicitly false (e.g. for local KTA calls), no proxy is applied.
        /// </summary>
        private HttpClient CreateHttpClient(int timeoutSeconds, bool? useProxy = null)
        {
            HttpClientHandler handler;

            bool applyProxy = (useProxy ?? true) && _cachedProxy != null;

            if (applyProxy)
            {
                handler = new HttpClientHandler
                {
                    Proxy = _cachedProxy,
                    UseProxy = true
                };
            }
            else
            {
                handler = new HttpClientHandler
                {
                    UseProxy = false
                };
            }

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        }

        /// <summary>
        /// Inlined server variable reader — no dependency on tungstenlabs.integration.resistantai.
        /// Returns a dictionary keyed by variable name with the value as a string.
        /// Throws if a variable is not found or is marked Secure (matches original ServerVariableHelper behaviour).
        /// </summary>
        private Dictionary<string, string> GetServerVariables(string taSessionId, string taSdkUrl, List<string> variableNames)
        {
            string url = $"{taSdkUrl}/ServerService.svc/json/GetServerVariables";
            var payload = new
            {
                sessionId = taSessionId,
                serverVariablesFilter = new
                {
                    CategoryIdentity = new { Id = "", Name = "" },
                    ServerIdentity = new { Id = "", Name = "" },
                    SearchText = ""
                }
            };

            using (HttpClient client = new HttpClient(new HttpClientHandler { UseProxy = false }))
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = client.PostAsync(url, content).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Error fetching server variables: {response.ReasonPhrase}");

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var json = JObject.Parse(body);
                var variables = json["d"] as JArray;

                var result = new Dictionary<string, string>();

                foreach (string name in variableNames)
                {
                    var match = variables?.FirstOrDefault(v => v["Identity"]?["Name"]?.ToString() == name);

                    if (match == null)
                        throw new Exception($"Server variable '{name}' was not found in the Server.");

                    bool isSecure = match["IsSecure"]?.ToString().ToLower() == "true";
                    if (isSecure)
                        throw new Exception($"Server variable '{name}' is set to secure; no value can be read.");

                    result[name] = match["Value"]?.ToString() ?? "";
                }

                return result;
            }
        }

        private DiagnosticResult Skipped(string stepName, string reason, string suggestedArea)
        {
            return new DiagnosticResult
            {
                StepName = stepName,
                Status = "Skipped",
                Passed = false,
                ErrorMessage = reason,
                SuggestedArea = suggestedArea
            };
        }

        /// <summary>
        /// Stamps the correct Status value onto a completed result before it is added to the list.
        /// Call this instead of results.Add(result) directly in each step.
        /// </summary>
        private void Finish(List<DiagnosticResult> results, DiagnosticResult result)
        {
            result.Status = result.Passed ? "Pass" : "Fail";
            results.Add(result);
        }

        private bool IsCertificateError(HttpRequestException ex)
        {
            Exception current = ex;
            while (current != null)
            {
                if (current.Message != null &&
                    (current.Message.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     current.Message.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     current.Message.IndexOf("TLS", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
                current = current.InnerException;
            }
            return false;
        }
    }
}