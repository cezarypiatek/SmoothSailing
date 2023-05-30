# SmoothSailing
A library to support Helm Chart installation in Kubernetes cluster from .NET code

![](logo_dark.png)

## How to install
`SmoothSailing` is distribute as a nuget package [SmoothSailing](https://www.nuget.org/packages/SmartAnalyzers.SmoothSailing/)

## Sample usage

```cs
using var chartInstaller = new ChartInstaller(new ProcessLauncher());
using var release = await _chartInstaller.Install
(
    chart: new ChartFromLocalPath("./charts/mysamplechart"),
    releaseName: "samplerelease",
    overrides: new {
    sample_property = "sample_value"
    },
    timeout: TimeSpan.FromMinutes(2)
);
```

## Packing helm chart into nuget package

1. Add your Helm Charts into `charts/` directory in your project
2. Edit project file to include all chart's file as `Content` in your nuget package
  ```xml
  <ItemGroup>
    <Content Include="charts/**/*.yaml">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
      <PackageCopyToOutput>true</PackageCopyToOutput>
    </Content>
  </ItemGroup>
  ```
 
3. Make sure that all `yaml` files are encoded as `UTF-8` not `UTF-8-BOOM`


