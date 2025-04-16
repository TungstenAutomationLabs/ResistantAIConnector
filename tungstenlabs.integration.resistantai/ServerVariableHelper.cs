using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace tungstenlabs.integration.resistantai
{
    internal class ServerVariableHelper
    {
        public Dictionary<string, KeyValuePair<string, string>> GetServerVariables(string taSessionId, string taSdkUrl, List<string> variableNames)
        {
            Dictionary<string, KeyValuePair<string, string>> result = new Dictionary<string, KeyValuePair<string, string>>();

            string getServerVariablesUrl = $"{taSdkUrl}/ServerService.svc/json/GetServerVariables";
            HttpClient httpClient = new HttpClient();
            var getRequestPayload = new
            {
                sessionId = taSessionId,
                serverVariablesFilter = new
                {
                    CategoryIdentity = new { Id = "", Name = "" },
                    ServerIdentity = new { Id = "", Name = "" },
                    SearchText = ""
                }
            };

            var getRequestContent = new StringContent(JsonConvert.SerializeObject(getRequestPayload), Encoding.UTF8, "application/json");
            var getResponse = httpClient.PostAsync(getServerVariablesUrl, getRequestContent).GetAwaiter().GetResult();

            if (!getResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching server variable: {getResponse.ReasonPhrase}");
            }

            var getResponseContent = getResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            // Parse the JSON into a JArray
            var getResponseJson = JObject.Parse(getResponseContent);
            var variables = getResponseJson["d"] as JArray;

            foreach (string s in variableNames)
            {
                // Find the variable with the matching name
                var matchingVariable = variables.FirstOrDefault(variable =>
                    variable["Identity"]?["Name"]?.ToString() == s);

                if (matchingVariable == null)
                    throw new Exception($"Server variable {s} was not found in the Server");

                var id = matchingVariable["Identity"]?["Id"]?.ToString();
                var value = matchingVariable["Value"].ToString();
                var isSecure = matchingVariable["IsSecure"].ToString();

                if (isSecure.ToLower().Equals("true"))
                    throw new Exception($"Server Variable '{s}' is set to secure; no value can be read.");

                result.Add(s, new KeyValuePair<string, string>(id, value));
            }

            return result;
        }

        public Dictionary<string, string> GetServerVariableIDandValue(string taSessionId, string taSdkUrl, string variableName)
        {
            string getServerVariablesUrl = $"{taSdkUrl}/ServerService.svc/json/GetServerVariables";
            HttpClient httpClient = new HttpClient();
            var getRequestPayload = new
            {
                sessionId = taSessionId,
                serverVariablesFilter = new
                {
                    CategoryIdentity = new { Id = "", Name = "" },
                    ServerIdentity = new { Id = "", Name = "" },
                    SearchText = variableName
                }
            };

            var getRequestContent = new StringContent(JsonConvert.SerializeObject(getRequestPayload), Encoding.UTF8, "application/json");
            var getResponse = httpClient.PostAsync(getServerVariablesUrl, getRequestContent).GetAwaiter().GetResult();

            if (!getResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching server variable: {getResponse.ReasonPhrase}");
            }

            var getResponseContent = getResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var getResponseJson = JObject.Parse(getResponseContent);
            var variable = getResponseJson["d"]?[0];

            if (variable == null)
            {
                throw new Exception($"Variable '{variableName}' not found.");
            }

            string variableId = variable["Identity"]?["Id"]?.ToString();
            string variableValue = variable["Value"].ToString();

            if (string.IsNullOrEmpty(variableId))
            {
                throw new Exception($"Variable ID for '{variableName}' not found.");
            }

            return new Dictionary<string, string>() { { variableId, variableValue } };
        }

        public bool UpdateServerVariables(Dictionary<string, string> keyValuePairs, string taSessionId, string taSdkUrl)
        {
            string updateServerVariablesUrl = $"{taSdkUrl}/ServerService.svc/json/UpdateServerVariables";
            HttpClient httpClient = new HttpClient();

            var updateRequestPayload = new
            {
                sessionId = taSessionId,
                updatedVariables = keyValuePairs.Select(kvp => new
                {
                    Id = kvp.Key,
                    Value = kvp.Value
                }).ToArray()
            };

            var updateRequestContent = new StringContent(JsonConvert.SerializeObject(updateRequestPayload), Encoding.UTF8, "application/json");
            var updateResponse = httpClient.PostAsync(updateServerVariablesUrl, updateRequestContent).GetAwaiter().GetResult();

            if (!updateResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Error updating server variable: {updateResponse.ReasonPhrase}");
            }

            return true;
        }
    }
}
