# Compilar online sin instalar .NET

Este proyecto incluye un workflow de GitHub Actions que genera un `GameProfileSwitcher.exe` x64 autocontenido.

## Pasos
1. Crear un repositorio nuevo en GitHub (puede ser privado).
2. Subir **el contenido de esta carpeta** a la raíz del repositorio.
3. Abrir la pestaña **Actions** y habilitar workflows si GitHub lo solicita.
4. Abrir **Build GameProfileSwitcher** y pulsar **Run workflow**.
5. Cuando termine, entrar al run y descargar el artifact **GameProfileSwitcher-v0.1-win-x64**.

El ZIP descargado del artifact contiene `GameProfileSwitcher.exe`. No requiere instalar .NET Runtime ni SDK en la PC de destino porque se publica como self-contained.
