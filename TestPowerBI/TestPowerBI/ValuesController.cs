using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using Microsoft.PowerBI.Api.V2;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Filters;
using System.Web.Http.Controllers;
using Microsoft.PowerBI.Api.V2.Models;

namespace TestPowerBI
{
    public class ValuesController
    {
        private static readonly string Username = ConfigurationManager.AppSettings["pbiUsername"];
        private static readonly string Password = ConfigurationManager.AppSettings["pbiPassword"];
        private static readonly string AuthorityUrl = ConfigurationManager.AppSettings["authorityUrl"];
        private static readonly string ResourceUrl = ConfigurationManager.AppSettings["resourceUrl"];
        private static readonly string ApplicationId = ConfigurationManager.AppSettings["ApplicationId"];
        private static readonly string ApiUrl = ConfigurationManager.AppSettings["apiUrl"];
        private static readonly string WorkspaceId = ConfigurationManager.AppSettings["workspaceId"];
        private static readonly string ReportId = ConfigurationManager.AppSettings["reportId"];

        /// <summary>
        /// Method to get token for embedding report
        /// </summary>
        /// <param name="username">username of the customer</param>
        /// <param name="roles">roles denoting rls specific access</param>
        /// <returns></returns>
        [Route("api/values/getReportEmbedToken")]
        [HttpGet]
        public async Task<HttpResponseMessage> GetReportEmbedToken(string username, string roles)
        {
            var result = new EmbedConfig();
            try
            {
                result = new EmbedConfig { Username = username, Roles = roles };
                var error = GetWebConfigErrors();
                if (error != null)
                {
                    result.ErrorMessage = error;
                    return this.Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                }
                // Create a user password cradentials.
                var credential = new UserPasswordCredential(Username, Password);
                var newCredential = CredentialCache.DefaultCredentials;

                // Authenticate using created credentials
                var authenticationContext = new AuthenticationContext(AuthorityUrl);
                var authenticationResult = await authenticationContext.AcquireTokenAsync(ResourceUrl, ApplicationId, credential);

                if (authenticationResult == null)
                {
                    result.ErrorMessage = "Authentication Failed.";
                    return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                }

                var tokenCredentials = new TokenCredentials(authenticationResult.AccessToken, "Bearer");

                // Create a Power BI Client object. It will be used to call Power BI APIs.
                using (var client = new PowerBIClient(new Uri(ApiUrl), tokenCredentials))
                {
                    // Get a list of reports.
                    var reports = await client.Reports.GetReportsInGroupAsync(WorkspaceId);

                    // No reports retrieved for the given workspace.
                    if (reports.Value.Count() == 0)
                    {
                        result.ErrorMessage = "No reports were found in the workspace";
                        return this.Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                    }

                    Report report;
                    if (string.IsNullOrWhiteSpace(ReportId))
                    {
                        // Get the first report in the workspace.
                        report = reports.Value.FirstOrDefault();
                    }
                    else
                    {
                        report = reports.Value.FirstOrDefault(r => r.Id == ReportId);
                    }

                    if (report == null)
                    {
                        result.ErrorMessage = "No report with the given ID was found in the workspace. Make sure ReportId is valid.";
                        return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                    }

                    var datasets = await client.Datasets.GetDatasetByIdInGroupAsync(WorkspaceId, report.DatasetId);
                    bool? IsEffectiveIdentityRequired = datasets.IsEffectiveIdentityRequired;
                    bool? IsEffectiveIdentityRolesRequired = datasets.IsEffectiveIdentityRolesRequired;
                    GenerateTokenRequest generateTokenRequestParameters;
                    // This is how you create embed token with effective identities
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        var rls = new EffectiveIdentity(username, new List<string> { report.DatasetId });
                        if (!string.IsNullOrWhiteSpace(roles))
                        {
                            var rolesList = new List<string>();
                            rolesList.AddRange(roles.Split(','));
                            rls.Roles = rolesList;
                        }
                        // Generate Embed Token with effective identities.
                        generateTokenRequestParameters = new GenerateTokenRequest(accessLevel: "view", identities: new List<EffectiveIdentity> { rls });
                    }
                    else
                    {
                        // Generate Embed Token for reports without effective identities.
                        generateTokenRequestParameters = new GenerateTokenRequest(accessLevel: "view");
                    }

                    var tokenResponse = await client.Reports.GenerateTokenInGroupAsync(WorkspaceId, report.Id, generateTokenRequestParameters);

                    if (tokenResponse == null)
                    {
                        result.ErrorMessage = "Failed to generate embed token.";
                        return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                    }

                    // Generate Embed Configuration.
                    result.EmbedToken = tokenResponse;
                    result.EmbedUrl = report.EmbedUrl;
                    result.Id = report.Id;

                    return Request.CreateResponse(HttpStatusCode.OK, result);
                }
            }
            catch (HttpOperationException exc)
            {
                result.ErrorMessage = string.Format("Status: {0} ({1})\r\nResponse: {2}\r\nRequestId: {3}", exc.Response.StatusCode, (int)exc.Response.StatusCode, exc.Response.Content, exc.Response.Headers["RequestId"].FirstOrDefault());
                return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
            }
            catch (Exception exc)
            {
                result.ErrorMessage = exc.ToString();
                return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
            }
        }

        /// <summary>
        /// Method to get token for embedding dashboard
        /// </summary>
        /// <returns>HttpResponse containing token details, else error response with message</returns>
        [Route("api/values/getDashboardEmbedToken")]
        [HttpGet]
        public async Task<HttpResponseMessage> GetDashboardEmbedToken()
        {
            var result = new EmbedConfig();
            try
            {
                var error = GetWebConfigErrors();
                if (error != null)
                {
                    result.ErrorMessage = error;
                    return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                }

                // Create a user password cradentials.
                var credential = new UserPasswordCredential(Username, Password);

                // Authenticate using created credentials
                var authenticationContext = new AuthenticationContext(AuthorityUrl);
                var authenticationResult = await authenticationContext.AcquireTokenAsync(ResourceUrl, ApplicationId, credential);

                if (authenticationResult == null)
                {
                    result.ErrorMessage = "Authentication Failed.";
                    return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                }

                var tokenCredentials = new TokenCredentials(authenticationResult.AccessToken, "Bearer");

                // Create a Power BI Client object. It will be used to call Power BI APIs.
                using (var client = new PowerBIClient(new Uri(ApiUrl), tokenCredentials))
                {
                    // Get a list of dashboards.
                    var dashboards = await client.Dashboards.GetDashboardsInGroupAsync(WorkspaceId);

                    // Get the first report in the workspace.
                    var dashboard = dashboards.Value.FirstOrDefault();

                    if (dashboard == null)
                    {
                        result.ErrorMessage = "Workspace has no dashboards.";
                        return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                    }

                    // Generate Embed Token.
                    var generateTokenRequestParameters = new GenerateTokenRequest(accessLevel: "view");
                    var tokenResponse = await client.Dashboards.GenerateTokenInGroupAsync(WorkspaceId, dashboard.Id, generateTokenRequestParameters);

                    if (tokenResponse == null)
                    {
                        result.ErrorMessage = "Failed to generate embed token.";
                        return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                    }

                    // Generate Embed Configuration.

                    result.EmbedToken = tokenResponse;
                    result.EmbedUrl = dashboard.EmbedUrl;
                    result.Id = dashboard.Id;


                    return Request.CreateResponse(HttpStatusCode.OK, result);
                }
            }
            catch (HttpOperationException exc)
            {
                result.ErrorMessage = string.Format("Status: {0} ({1})\r\nResponse: {2}\r\nRequestId: {3}", exc.Response.StatusCode, (int)exc.Response.StatusCode, exc.Response.Content, exc.Response.Headers["RequestId"].FirstOrDefault());
                return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
            }
            catch (Exception exc)
            {
                result.ErrorMessage = exc.ToString();
                return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
            }
        }

        /// <summary>
        /// Method to get token for embedding tile
        /// </summary>
        /// <returns>HttpResponse containing token details, else error response with message</returns>
        [Route("api/values/getTileEmbedToken")]
        [HttpGet]
        public async Task<HttpResponseMessage> GetTileEmbedToken()
        {
            var result = new TileEmbedConfig();
            try
            {
                var error = GetWebConfigErrors();
                if (error != null)
                {
                    result.ErrorMessage = error;
                    return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                }

                // Create a user password cradentials.
                var credential = new UserPasswordCredential(Username, Password);

                // Authenticate using created credentials
                var authenticationContext = new AuthenticationContext(AuthorityUrl);
                var authenticationResult = await authenticationContext.AcquireTokenAsync(ResourceUrl, ApplicationId, credential);

                if (authenticationResult == null)
                {
                    result.ErrorMessage = "Authentication Failed.";
                    return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                }

                var tokenCredentials = new TokenCredentials(authenticationResult.AccessToken, "Bearer");

                // Create a Power BI Client object. It will be used to call Power BI APIs.
                using (var client = new PowerBIClient(new Uri(ApiUrl), tokenCredentials))
                {
                    // Get a list of dashboards.
                    var dashboards = await client.Dashboards.GetDashboardsInGroupAsync(WorkspaceId);

                    // Get the first report in the workspace.
                    var dashboard = dashboards.Value.FirstOrDefault();

                    if (dashboard == null)
                    {
                        result.ErrorMessage = "Workspace has no dashboards.";
                        return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                    }

                    var tiles = await client.Dashboards.GetTilesInGroupAsync(WorkspaceId, dashboard.Id);

                    // Get the first tile in the workspace.
                    var tile = tiles.Value.FirstOrDefault();

                    // Generate Embed Token for a tile.
                    var generateTokenRequestParameters = new GenerateTokenRequest(accessLevel: "view");
                    var tokenResponse = await client.Tiles.GenerateTokenInGroupAsync(WorkspaceId, dashboard.Id, tile.Id, generateTokenRequestParameters);

                    if (tokenResponse == null)
                    {
                        result.ErrorMessage = "Failed to generate embed token.";
                        return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
                    }

                    // Generate Embed Configuration.
                    result.EmbedToken = tokenResponse;
                    result.EmbedUrl = tile.EmbedUrl;
                    result.Id = tile.Id;
                    result.dashboardId = dashboard.Id;

                    return Request.CreateResponse(HttpStatusCode.OK, result);
                }
            }
            catch (HttpOperationException exc)
            {
                result.ErrorMessage = string.Format("Status: {0} ({1})\r\nResponse: {2}\r\nRequestId: {3}", exc.Response.StatusCode, (int)exc.Response.StatusCode, exc.Response.Content, exc.Response.Headers["RequestId"].FirstOrDefault());
                return Request.CreateResponse(HttpStatusCode.InternalServerError, result);
            }
            catch (Exception exc)
            {
                result.ErrorMessage = exc.ToString();
                return this.Request.CreateResponse(HttpStatusCode.InternalServerError, result);
            }
        }

        /// <summary>
        /// Check if web.config embed parameters have valid values.
        /// </summary>
        /// <returns>Null if web.config parameters are valid, otherwise returns specific error string.</returns>
        private string GetWebConfigErrors()
        {
            // Application Id must have a value.
            if (string.IsNullOrWhiteSpace(ApplicationId))
            {
                return "ApplicationId is empty. please register your application as Native app in https://dev.powerbi.com/apps and fill client Id in web.config.";
            }

            // Application Id must be a Guid object.
            Guid result;
            if (!Guid.TryParse(ApplicationId, out result))
            {
                return "ApplicationId must be a Guid object. please register your application as Native app in https://dev.powerbi.com/apps and fill application Id in web.config.";
            }

            // Workspace Id must have a value.
            if (string.IsNullOrWhiteSpace(WorkspaceId))
            {
                return "WorkspaceId is empty. Please select a group you own and fill its Id in web.config";
            }

            // Workspace Id must be a Guid object.
            if (!Guid.TryParse(WorkspaceId, out result))
            {
                return "WorkspaceId must be a Guid object. Please select a workspace you own and fill its Id in web.config";
            }

            // Username must have a value.
            if (string.IsNullOrWhiteSpace(Username))
            {
                return "Username is empty. Please fill Power BI username in web.config";
            }

            // Password must have a value.
            if (string.IsNullOrWhiteSpace(Password))
            {
                return "Password is empty. Please fill password of Power BI username in web.config";
            }

            return null;
        }
    }
}
