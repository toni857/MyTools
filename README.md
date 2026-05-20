# MyTools

Diese Repository enthaelt die Bibliothek `MyTools` als eigenen Ordner.

## Verwendung

1. Repository als ZIP herunterladen oder klonen.
2. Den Ordner `MyTools` in deine Solution kopieren.
3. Das Projekt `MyTools/My.csproj` in deine Solution aufnehmen.
4. In deinem Hauptprojekt eine Project Reference auf `MyTools/My.csproj` setzen.
5. Danach kannst du `using My;` verwenden.

## Beispiel

```csharp
using My;
using Console = My.Console;

Console.Write("Vor Line()");
Console.Line();
Console.LineWrite("Text nach leerer Zeile");
Console.Line();
```
