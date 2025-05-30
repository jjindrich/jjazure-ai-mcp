# ASP.Net Core web app call API with JWT authentication

## Setup Entra ID App registrations

You need to create two app registrations in Entra ID (Azure AD) for the web app and the API. Follow these steps:
- jjwebsec for web app
- jjwebapisec for API

Configure app registration for web app in Entra ID:
- add redirect URIs https://localhost:7045/signin-oidc
- create client secret and paste it in appsettings.json
- paste the client ID in appsettings.json

Configure app registration for API in Entra ID:
- update client ID in appsettings.json values ClientId and Audience
- update client ID in source code of web app calling API