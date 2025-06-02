using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using System.Net.Http;

namespace jjwebsec.Pages
{
    [Authorize]
    [AuthorizeForScopes(Scopes = new string[] { "api://4ddc5c46-ed4c-451c-b45b-dbbf3cf86ae3/.default" })]
    public class CallApiModel : PageModel
    {
        private readonly ILogger<CallApiModel> _logger;
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly HttpClient _httpClient;

        public CallApiModel(
            ILogger<CallApiModel> logger,
            ITokenAcquisition tokenAcquisition,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _tokenAcquisition = tokenAcquisition;
            _httpClient = httpClientFactory.CreateClient("ApiClient");
        }
                
        public async Task<IActionResult> OnGetAsync()
        {
            string token = null;
            try
            {
                // client secret is required for confidential clients
                token = await _tokenAcquisition.GetAccessTokenForUserAsync(new string[] { "api://4ddc5c46-ed4c-451c-b45b-dbbf3cf86ae3/.default" });
            }
            catch (MicrosoftIdentityWebChallengeUserException ex)
            {
                return Challenge();
            }

            if (token == null)
            {
                _logger.LogWarning("Token retrieval failed.");
                return Page();
            } 
            else
            {
                _logger.LogInformation("Access Token: {Token}", token);
            }

            ViewData["AccessToken"] = token;

            using (_httpClient)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.GetAsync("/api/values");
                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = await response.Content.ReadAsStringAsync();
                    ViewData["ApiMessage"] = apiResponse;
                }
                else
                {
                    ViewData["ApiMessage"] = "Failed to retrieve message from API. Status Code: " + response.StatusCode;
                }
            }

            return Page();
        }
    }
}
