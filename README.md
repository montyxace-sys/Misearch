# Misearch

Navegador de escritorio para Windows centrado en privacidad: bloquea rastreadores, impide el acceso a cámara, micrófono y ubicación, y permite encender una VPN WireGuard solo para el navegador. Sin cuentas, sin sincronización en la nube y sin telemetría.

![Ventana principal de Misearch](docs/main.png)

## Descargas

Última versión: **0.1.0**. Todas las versiones están en [Releases](https://github.com/montyxace-sys/Misearch/releases).

| Formato | Descarga directa |
| --- | --- |
| Portable (ZIP, x64) | [Misearch-0.1.0-win-x64.zip](https://github.com/montyxace-sys/Misearch/releases/download/v0.1.0/Misearch-0.1.0-win-x64.zip) |

Comprueba la integridad de lo descargado con [SHA256SUMS.txt](https://github.com/montyxace-sys/Misearch/releases/download/v0.1.0/SHA256SUMS.txt).

El ZIP contiene `Misearch.exe` (autocontenido, no necesita .NET instalado) y la carpeta `Assets`. Descomprime todo junto y ejecuta `Misearch.exe`. Necesita el runtime de Microsoft Edge WebView2, incluido de serie en Windows 10 y 11 actualizados.

## Funciones

- Pestañas, barra de direcciones, marcadores, historial local y descargas con progreso.
- Buscador configurable: **DuckDuckGo por defecto**, con Google, Bing, Startpage y Brave como alternativas.
- Bloqueo de rastreadores y publicidad con lista integrada y lista propia ampliable.
- HTTPS primero: si un sitio no admite HTTPS se avisa y se ofrece la versión no cifrada.
- Modo privado y borrado de datos al cerrar (caché, cookies, historial y almacenamiento).
- Permisos denegados por defecto: cámara, micrófono, ubicación y notificaciones.
- VPN WireGuard o perfil Clash importable, aplicada únicamente al navegador mediante el core [Mihomo](https://github.com/MetaCubeX/mihomo).
- Atajos de teclado: `Ctrl+T`, `Ctrl+W`, `Ctrl+L`, `Ctrl+R`, `Ctrl+Shift+Supr`, `Ctrl++`, `Ctrl+-`, `Ctrl+0`, `Alt+←`, `Alt+→`.
- Interfaz oscura con acento naranja y zoom por pestaña.

## Cómo se usa

1. Abre `Misearch.exe`. La primera pestaña muestra el buscador y los accesos rápidos.
2. Escribe una dirección o una búsqueda y pulsa Enter. Las direcciones locales (`localhost`, `192.168.x.x`) se abren por HTTP; el resto, por HTTPS.
3. Para la VPN, abre el panel **VPN**, importa un `.conf` de WireGuard o un YAML de Clash y pulsa **Conectar**. La primera vez se descarga el core Mihomo desde su repositorio oficial.
4. Los paneles de **Privacidad**, **Historial**, **Marcadores**, **Descargas** y **Ajustes** están en la barra superior y en el menú.

## Privacidad y seguridad

- El bloqueo de rastreadores se aplica a todas las peticiones de la pestaña y cuenta los bloqueos en la barra de estado.
- Las páginas no pueden abrir ventanas nuevas por su cuenta ni cambiar la dirección sin pasar por el navegador.
- Los permisos sensibles se deniegan sin preguntar; solo se permite leer el portapapeles cuando el usuario lo pide en una página.
- El modo privado viene activado y, al cerrar, se eliminan los datos de navegación del perfil.
- El historial y los marcadores se guardan solo en el equipo (`%AppData%\Misearch`), nunca se envían fuera.
- No hay cuentas, analítica ni servidores de MSoft implicados.

### VPN y kill switch

La VPN se aplica con `--proxy-server` de WebView2 apuntando al core local de Mihomo. Si el core se detiene o el perfil falla, el navegador se queda sin ruta: no hay fuga directa. El core siempre arranca en modo DIRECT cuando está instalado, de modo que activar o desactivar la VPN no deja pestañas sin proxy.

El core Mihomo es software libre GPL-3.0 y **no se redistribuye**: se descarga en tiempo de ejecución desde las releases oficiales de MetaCubeX, verificando el SHA-256 publicado.

## Limitaciones de esta versión (0.1)

- Solo Windows 10/11 de 64 bits.
- Una ventana y un perfil: todas las pestañas comparten sesión y proxy.
- Sin extensiones, sin sincronización y sin lector de PDF integrado (se usa el del sistema).
- La VPN cubre solo el tráfico del navegador, no el resto del sistema.
- La VPN necesita un perfil WireGuard o Clash válido; no incluye servidores propios.
- La lista de bloqueo es estática; no hay suscripciones remotas.

## Compilar

Requisitos: Windows y el SDK de .NET 8.

```
dotnet build App/Misearch.App.csproj -c Release
```

Para generar el ejecutable autocontenido que se publica:

```
dotnet publish App/Misearch.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/Misearch
```

## Licencia

MIT. Consulta [LICENSE](LICENSE).
