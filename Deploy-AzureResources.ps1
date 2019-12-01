#Requires -Modules Az

param(
    [Parameter( Mandatory )]
    $resourceGroupName,

    [Parameter( Mandatory )]
    $location
)

$ErrorActionPreference = 'Stop'

Connect-AzAccount

New-AzResourceGroup -Name $resourceGroupName -Location "$location"
New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile ./azuredeploy.json
