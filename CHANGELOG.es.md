# Historial de versiones

Todos los cambios importantes de Citadel para Windows. Las versiones siguen el
[versionado semántico](https://semver.org/lang/es/): mayor.menor.parche.

[English](CHANGELOG.md) · Español · [日本語](CHANGELOG.ja.md)

## 1.4.0 - 2026-11-27
### Agregado

- **Interfaz en japonés**: Citadel y su ventana de instalación ahora están en inglés, en español y en japonés.
  El texto en japonés usa las fuentes japonesas de Windows (Yu Gothic UI, o Meiryo UI).
- El idioma de la interfaz también se elige en Ajustes → Apariencia, bajo «Idioma / Language / 言語».
- Abrir Citadel cuando ya está abierto trae su ventana al frente (restaurada si estaba minimizada), en lugar
  de no hacer nada.
- La ventana se abre maximizada si estaba maximizada cuando se cerró Citadel.
- Todos los botones, también los que son solo un ícono (conversación nueva, Ajustes, Acerca de, exportar,
  idioma, Razonar, enviar, Copiar, el bloque del razonamiento, los colores), se alcanzan con Tab y se pulsan
  con Espacio o Enter; el que tiene el foco muestra un borde del color de énfasis. Un clic con el mouse sigue
  dejando el foco en el cuadro de texto.
- Los enlaces de las respuestas muestran su dirección al pasar el puntero por encima.
- Los botones que solo tienen un ícono (conversación nueva, Acerca de, exportar, idioma, enviar y detener,
  Razonar, Copiar, eliminar) tienen nombres que leen los lectores de pantalla, como el Narrador.
- La instalación también deja `README.ja.txt`, el README en japonés, en la carpeta del programa.
- README, manual en PDF e historial de versiones en japonés, y el historial de versiones en español.

### Cambiado

- Sin un idioma elegido en la instalación (el `Citadel.exe` independiente), el primer arranque usa el idioma
  de Windows si es español o japonés, en lugar de siempre inglés.
- Un adjunto puede pesar como máximo 2 MB; un archivo más grande se rechaza con un aviso. El texto de un
  adjunto se lee en su propia codificación (marca de orden de bytes, UTF-8 o la página de códigos de Windows
  para programas que no son Unicode), así un archivo que no es UTF-8 ya no muestra caracteres rotos.
- `scripts\build.ps1` ya no escribe `dist\SHA256SUMS.txt`: las sumas y su firma (`release.sig`) se calculan
  y se publican aparte al crear la versión.
- `--screenshot` muestra las rutas de la carpeta dada con `--data-folder` como `%APPDATA%\Citadel`, así las
  imágenes de los manuales muestran la ruta real.
- `--self-test` también comprueba que cada texto de la interfaz esté en los tres idiomas con las mismas
  marcas, las etiquetas `<think>` partidas entre fragmentos de una respuesta y las codificaciones de los
  adjuntos.
- Las conversaciones, los ajustes, las reglas y las memorias se escriben completos en el disco antes de
  reemplazar el archivo anterior, así un corte de luz ya no puede dejar un archivo vacío.

### Corregido

- Cambiar el idioma o el tema con Ajustes abierto descartaba las reglas que se estaban editando sin guardar.
- Una etiqueta `<think>` que llegaba partida entre dos fragmentos de una respuesta se mostraba como texto y el
  razonamiento se mezclaba con la respuesta.
- Una conversación eliminada mientras se escribía su respuesta volvía a aparecer en el arranque siguiente;
  ahora su respuesta se detiene.
- Un enlace con una dirección inválida podía impedir que se dibujara una respuesta; ahora se muestra como
  texto común.
- Al exportar una conversación cuyo título es un nombre que Windows reserva (CON, NUL, COM1…), ahora se
  guarda como «Citadel - <título>.md», y un título largo con emojis ya no se corta en mitad de un carácter.
- Con Ollama o la computadora de SSH inaccesibles, las comprobaciones de estado ya no se acumulan.
- Una respuesta de Ollama que no se puede leer (por ejemplo, de otro programa en su puerto) ahora da el mensaje
  «Ollama devolvió una respuesta que no se entiende.» en lugar de un error técnico.
- La búsqueda en los mensajes muestra como máximo 200 resultados, como estaba previsto (podía mostrar 201).

## 1.3.2 - 2026-11-27

### Cambiado

- **Un ícono nuevo**: un castillo con su torre del homenaje y dos torres, y un globo de diálogo como
  banderín, dibujado plano, color crema con contorno negro sobre el color elegido; sigue el color elegido en
  los ajustes, en los doce colores. El dibujo está en `icon/citadel.svg`.

## 1.3.1 - 2026-11-25

### Cambiado

- **Actualizaciones firmadas por el autor**: el `SHA256SUMS.txt` de una versión ahora tiene que llevar la
  firma del autor (`release.sig`, ECDSA P-256, comprobada con dos claves que trae Citadel) antes de
  descargar el instalador. Antes, el instalador se comprobaba solo con los hashes de la misma página, así que
  una versión puesta ahí por otra persona se habría instalado sola. Una versión sin firma válida se ofrece
  como página, para descargarla a mano. Cualquiera puede seguir descargando, instalando y compartiendo
  Citadel; Citadel 1.3.0 y anteriores instalan esta como siempre.

## 1.3.0 - 2026-11-20

### Agregado

- Actualizaciones que se instalan solas, como en Gryphmark: una vez por día (y desde Acerca de → Buscar
  actualizaciones) Citadel busca una versión nueva en GitHub y pregunta una sola vez: Actualizar ahora, Ver
  novedades o Más tarde. Actualizar ahora descarga `citadel-setup.exe`, lo comprueba con el
  `SHA256SUMS.txt` de la versión (un archivo que no coincide se borra y nunca se ejecuta), espera o detiene
  una respuesta en curso según lo que elija el usuario, se cierra, instala en silencio conservando el idioma
  elegido al instalar y vuelve a abrir Citadel con el texto que se estaba escribiendo y la conversación
  abierta. El `Citadel.exe` independiente solo ofrece la página de la versión.
- Búsqueda en el texto de todas las conversaciones guardadas, sin distinguir mayúsculas, tildes ni saltos de
  línea: los resultados muestran la conversación, la fecha y las palabras alrededor de la coincidencia,
  resaltada, y un clic abre la conversación en ese mensaje. Ctrl+F va al campo de búsqueda.
- Exportar una conversación a Markdown, desde el encabezado o desde el menú de la barra lateral: título,
  fecha, modelo y cada turno bajo su propio encabezado, con los bloques de código intactos; «Abrir después
  de exportar» abre el archivo con la aplicación que Windows usa para los `.md`.
- La instalación acepta `/CITADELWAITPID=<pid>` y `/CITADELRELAUNCH=1`, que usan las actualizaciones.
- `scripts\build.ps1` también escribe `dist\SHA256SUMS.txt`.
- `--self-test` comprueba primero la búsqueda, la exportación y las comprobaciones de las actualizaciones,
  sin Ollama; `--self-test --offline` corre solo esas.

### Cambiado

- El campo de búsqueda, que solo miraba los títulos, también lista las coincidencias en los mensajes.

## 1.2.0 - 2026-11-10

### Agregado

- Tema y color, como en Wyrmhold y Gryphmark: Ajustes → Apariencia elige como Windows, claro u oscuro (el
  de fábrica), y uno de doce colores para los botones, la selección y el ícono de la ventana y de la barra
  de tareas (el verde es el original). Los controles estándar y la barra de título siguen la apariencia, y
  «como Windows» sigue al instante un cambio en Windows. El color de énfasis ya no sale del color de énfasis
  de Windows.

## 1.1.0 - 2026-11-09

### Agregado

- Ollama en otra computadora por SSH: Ajustes → Ollama abre un túnel con el comando `ssh` de Windows hasta
  el puerto 11434 de esa computadora y lo cierra al salir.
- Los modelos que no saben razonar aparecen como «(sin Razonar)» en la lista de modelos, y el botón Razonar
  queda atenuado mientras uno de ellos está elegido.

### Cambiado

- El botón de idioma pasó de abajo en la barra lateral a arriba a la derecha, junto a la lista de modelos, y
  muestra el idioma actual.

### Corregido

- El botón de idioma no hacía nada al hacer clic.
- Cambiar el idioma desde la ventana principal podía dejarla a medio reconstruir, y el texto de bienvenida
  quedaba en el idioma anterior.

### Quitado

- El menú Traducir.

## 1.0.0 - 2026-11-09

Primera versión.

### Agregado

- Chat con los modelos instalados en Ollama, que aparecen a medida que se escriben y con formato Markdown,
  con botones Copiar para cada respuesta y cada bloque de código.
- Botón Razonar: el modelo piensa primero y su razonamiento se muestra en un bloque plegado. Las respuestas
  tienen un tope de 8.000 tokens con Razonar activo y de 3.000 sin él.
- Botón Detener (Esc), que cierra la conexión para que Ollama deje de generar.
- Línea de estado: si Ollama responde, si el modelo está cargado, qué está haciendo, los segundos
  transcurridos y la velocidad de la última respuesta.
- Conversaciones en una barra lateral, guardadas como archivos JSON con el mismo formato que la versión para
  macOS, con búsqueda por título, renombrar y eliminar.
- Archivo de reglas que va al principio de cada pedido, editable en Ajustes.
- Memorias: `/mem <tema>` carga una memoria guardada en la conversación; `/save` redacta una con el modelo,
  la muestra para revisarla y la escribe, con su línea del índice, solo después de confirmarla, y comprueba
  las dos en el disco.
- Adjuntos de archivos `.txt` y `.md` arrastrándolos, y un menú Traducir.
- Interfaz en inglés y en español, que se elige en el menú de idioma (Idioma / Language).
- Estilo oscuro de Windows 11 con el color de énfasis de Windows.
- Programa de instalación en inglés y en español, un paquete MSI para instalar sin ventanas y un
  `Citadel.exe` independiente.
- Modos `--self-test` y `--screenshot` para mantenimiento y para las imágenes del manual.
- README y manual en PDF en inglés y en español.
