using ContainerRegistryTransfer.Helpers;
using ContainerRegistryTransfer.Models;
using Microsoft.Azure.Management.ContainerRegistry;
using Microsoft.Azure.Management.ContainerRegistry.Models;
using Microsoft.Azure.Management.KeyVault;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using System;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace ContainerRegistryTransfer.Clients
{
    internal class ExportClient
    {
        ContainerRegistryManagementClient registryClient;
        KeyVaultManagementClient keyVaultClient;
        Options options;

        public ExportClient(ContainerRegistryManagementClient registryClient, KeyVaultManagementClient keyVaultClient, Options options)
        {
            this.registryClient = registryClient;
            this.keyVaultClient = keyVaultClient;
            this.options = options;
        }

        public async Task<ExportPipeline> CreateExportPipelineAsync()
        {
            var exportPipelineName = options.ExportPipeline.PipelineName;
            Console.WriteLine($"Creating exportPipeline {exportPipelineName}.");
            var exportPipeline = await CreateExportPipelineResourceAsync().ConfigureAwait(false);
            Console.WriteLine($"Successfully created exportPipeline {exportPipelineName}.");

            // give the pipeline identity access to the key vault
            await KeyVaultHelper.AddKeyVaultAccessPolicyAsync(
                keyVaultClient,
                exportPipelineName,
                options.TenantId,
                options.ExportPipeline.ResourceGroupName,
                options.ExportPipeline.KeyVaultUri,
                IdentityHelper.GetManagedIdentityPrincipalId(exportPipeline.Identity));

            return exportPipeline;
        }

        public async Task<ExportPipeline> CreateExportPipelineResourceAsync()
        {
            var exportResourceGroupName = options.ExportPipeline.ResourceGroupName;
            var exportRegistryName = options.ExportPipeline.RegistryName;

            var registry = await registryClient.Registries.GetAsync(
                exportResourceGroupName,
                exportRegistryName).ConfigureAwait(false);

            if (registry != null)
            {
                var exportPipeline = new ExportPipeline(
                name: options.ExportPipeline.PipelineName,
                location: registry.Location,
                identity: IdentityHelper.GetManagedIdentity(options.ExportPipeline.UserAssignedIdentity),
                target: new ExportPipelineTargetProperties
                {
                    Type = "AzureStorageBlobContainer",
                    Uri = options.ExportPipeline.ContainerUri,
                    KeyVaultUri = options.ExportPipeline.KeyVaultUri
                },
                options: options.ExportPipeline.Options
                );

                return await registryClient.ExportPipelines.CreateAsync(registryName: options.ExportPipeline.RegistryName,
                                                                resourceGroupName: options.ExportPipeline.ResourceGroupName,
                                                                exportPipelineName: options.ExportPipeline.PipelineName,
                                                                exportPipelineCreateParameters: exportPipeline).ConfigureAwait(false);
            }
            else
            {
                throw new ArgumentException($"Could not find registry '{exportRegistryName}'. Please ensure the registry exists in the current resource group {exportResourceGroupName}.");
            }
        }

        public async Task ExportImagesAsync(ExportPipeline exportPipeline)
        {
            var pipelineId = exportPipeline.Id;
            var pipelineRunName = options.ExportPipelineRun.PipelineRunName;
            var targetName = options.ExportPipelineRun.TargetName;
            var artifacts = options.ExportPipelineRun.Artifacts;

            Console.WriteLine($"Export PipelineRun properties:");
            Console.WriteLine($"  registryName: {options.ExportPipeline.RegistryName}");
            Console.WriteLine($"  pipelineRunName: {options.ExportPipelineRun.PipelineRunName}");
            Console.WriteLine($"  pipelineResourceId: {pipelineId}");
            Console.WriteLine($"  targetName: {options.ExportPipelineRun.TargetName}");
            Console.WriteLine($"  artifacts: {string.Join(Environment.NewLine, artifacts)}");
            Console.WriteLine($"======================================================================");

            var pipelineRunRequest = new PipelineRunRequest
            {
                PipelineResourceId = pipelineId,
                Target = new PipelineRunTargetProperties
                {
                    Type = "AzureStorageBlob",
                    Name = targetName
                },
                Artifacts = artifacts
            };

            Console.WriteLine($"Running pipelineRun {pipelineRunName}...");

            var pipelineRun = await registryClient.PipelineRuns.CreateAsync(registryName: options.ExportPipeline.RegistryName,
                                                            resourceGroupName: options.ExportPipeline.ResourceGroupName,
                                                            pipelineRunName: pipelineRunName,
                                                           request: pipelineRunRequest).ConfigureAwait(false);

            if (string.Equals(pipelineRun.ProvisioningState, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"PipelineRun {pipelineRunName} failed with the inner error '{pipelineRun.Response.PipelineRunErrorMessage}'.");
            }
            else
            {
                Console.WriteLine($"PipelineRun {pipelineRunName} completed successfully!");
                Console.WriteLine($"Uploaded blob {targetName} to {options.ExportPipeline.ContainerUri}.");
            }
        }
    }
}
