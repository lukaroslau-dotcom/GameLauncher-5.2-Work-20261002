using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace GameLauncher.Service;
public sealed class CatalogCard(CatalogResult result) : INotifyPropertyChanged
{
 public CatalogResult Result { get; } = result;
 public string Title => Result.Title;
 public string Creator => Result.Creator;
 public F95Metadata? Metadata { get; set; }
 private string coverPath="",status="Cover wird geladen …";
 public string CoverPath {get=>coverPath;set{coverPath=value;Changed();}}
 public string Status {get=>status;set{status=value;Changed();}}
 public Task? Loading {get;set;}
 public event PropertyChangedEventHandler? PropertyChanged;
 private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
