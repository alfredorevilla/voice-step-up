using './entra.bicep'

// Non-identifying names only. scripts/entra-deploy.sh may override the identifier URI.
param apiUniqueName = 'voice-step-up-api'
param spaUniqueName = 'voice-step-up-spa'
param apiIdentifierUri = 'api://voice-step-up-api'
