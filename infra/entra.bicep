// Entra app registrations for voice-step-up: an API app and a SPA app.
// Microsoft Graph Bicep (preview): no what-if, no deployment stacks, no passwordCredentials.
extension microsoftGraphV1

@description('Fixed alternate key of the API application (idempotent redeploys).')
param apiUniqueName string

@description('Fixed alternate key of the SPA application.')
param spaUniqueName string

@description('Display name of the API application.')
param apiDisplayName string = 'voice-step-up API'

@description('Display name of the SPA application.')
param spaDisplayName string = 'voice-step-up SPA'

@description('App ID URI of the API, for example api://<unique name>. Must be globally unique.')
param apiIdentifierUri string

@description('SPA redirect URIs (authorization code with PKCE, type spa).')
param spaRedirectUris array = [
  'http://localhost:5173/'
]

// Fixed ids so the scope and role keep their identity across redeploys. Not secrets.
var scopeId = '6f1b7c0e-2d44-4b0a-9a53-0f6c3a1e5d11'
var analystRoleId = '3c9a2b58-71e0-4f6d-8b27-5d4e9c0a7b32'

resource spaApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: spaUniqueName
  displayName: spaDisplayName
  signInAudience: 'AzureADMyOrg'
  spa: {
    redirectUris: spaRedirectUris
  }
}

resource apiApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: apiUniqueName
  displayName: apiDisplayName
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    apiIdentifierUri
  ]
  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: scopeId
        value: 'access_as_user'
        type: 'User'
        isEnabled: true
        adminConsentDisplayName: 'Access voice-step-up as the signed-in user'
        adminConsentDescription: 'Allows the app to call the voice-step-up API on behalf of the signed-in user.'
        userConsentDisplayName: 'Access voice-step-up as you'
        userConsentDescription: 'Allows the app to call the voice-step-up API on your behalf.'
      }
    ]
    preAuthorizedApplications: [
      {
        appId: spaApp.appId
        delegatedPermissionIds: [
          scopeId
        ]
      }
    ]
  }
  appRoles: [
    {
      id: analystRoleId
      value: 'Analyst'
      displayName: 'Analyst'
      description: 'Reads step-up attempts and manages thresholds.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
  ]
  optionalClaims: {
    accessToken: [
      {
        name: 'amr'
        essential: false
      }
      {
        name: 'auth_time'
        essential: false
      }
      {
        name: 'idtyp'
        essential: false
      }
    ]
  }
}

resource apiSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: apiApp.appId
}

resource spaSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: spaApp.appId
}

output apiClientId string = apiApp.appId
output spaClientId string = spaApp.appId
output apiScope string = '${apiIdentifierUri}/access_as_user'
