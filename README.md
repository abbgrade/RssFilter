# RSS Filter

This RSS filter works with your existing RSS workflow and tool.
You can remove sponsored content or unwanted catagories by replacing the feed URL by a custom URL to your Azure Function that filters the feed on demand using a simple configuration file.

Create for every custom feed a configuration in a Azure Blob Storage Container.
See the [example configuration](./example/heise-ticker.xml).

## Installation and Deployment

### Azure Resources

The Azure resources can be created using the [ARM template](./azuredeploy.json).

### Azure Function App

- The Function App can be deployed using the Azure Functions VSCode Extension or Visual Studio.
- Alternatively using GitHub Actions.
The [workflow](./.github/workflows/build.yml) will not work without configuring the [GitHub Azure integration](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-github-actions).

### Filter Configurations / Usage

I recomment to setup a private GitHub repository that contains the XML filter configuration files.
The changes can be deployed automatically to Azure Blob Storage using GitHub Actions with the following workflow definition.

The connection string is secured using [the secrets feature](https://help.github.com/en/actions/automating-your-workflow-with-github-actions/creating-and-using-encrypted-secrets).

```yaml
name: Deploy to Azure Blob Storage

on: [push]

jobs:
  deploy:
    runs-on: ubuntu-latest
    env:
      AZURE_STORAGE_CONNECTION_STRING: ${{ secrets.AZURE_STORAGE_CONNECTION_STRING }}
      CONTAINER: filter
    steps:
    - uses: actions/checkout@v1
    - name: cleanse
      run: az storage blob delete-batch --source $CONTAINER --connection-string $AZURE_STORAGE_CONNECTION_STRING
    - name: upload
      run: az storage blob upload-batch --destination $CONTAINER --source "$GITHUB_WORKSPACE" --pattern "*.xml" --connection-string $AZURE_STORAGE_CONNECTION_STRING
```
