# Gainscha label preset templates (.sds)

Templates Seagull Driver Settings importados via `ssdal settings import` após criar a fila.

## Driver de referência

- Pacote: `drivers/Gainscha` (Seagull `2021.1.4_GN`, modelo `Gainscha GA-2408T`)
- Impressora de teste na rede: `10.1.152.132:9100` (RAW)

## Captura de templates (spike)

Num PC Windows **com o driver instalado** e uma fila de teste:

1. Preferências → Configuração de página → excluir stocks extra; deixar só USER com dimensões do setor.
2. Exportar:

```powershell
$ssdal = Get-ChildItem -Path "${env:ProgramFiles}","${env:ProgramFiles(x86)}" -Filter ssdal.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
ssdal /p "NomeDaFila" settings export "C:\Temp\paciente.sds"
```

3. Repetir para Pulseira (25×270), Matrix (50×30), Paciente (89×36), Dupla (45×13).
4. Copiar os `.sds` para:
   - `drivers/Gainscha/label-presets/*.sds`
   - `src/PrinterInstall.Core/Gainscha/Templates/*.sds` (embedded resources)

## Ficheiros

| Ficheiro | Preset | Dimensões |
|----------|--------|-----------|
| `pulseira.sds` | Pulseira | 25 × 270 mm |
| `matrix.sds` | Matrix | 50 × 30 mm |
| `paciente.sds` | Paciente | 89 × 36 mm |
| `dupla.sds` | Dupla | 45 × 13 mm |

Os `.sds` incluídos inicialmente são **placeholders** (stock USER com Data genérico). Substituir pelos exports reais antes de validação em produção.
