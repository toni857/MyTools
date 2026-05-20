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

## Kamera

```csharp
using My;

string foto = ConsoleImages.GreifeAufKameraZuUmFotoZuMachen();
ConsoleImages.ShowPhoto(foto);

ConsoleImages.CameraVideoSource kamera =
    ConsoleImages.GreifeAufKameraZuUmVideoZuMachen(durationSeconds: 10);

ConsoleImages.ShowVideo(kamera);
```

Ohne `durationSeconds` laeuft das Kamera-Video, bis das Programm beendet wird.
Mit `durationSeconds` stoppt `ShowVideo` nach dieser Zeit.

Standardmaessig wird unter Linux `/dev/video0` ueber `ffmpeg` und `v4l2` verwendet.
