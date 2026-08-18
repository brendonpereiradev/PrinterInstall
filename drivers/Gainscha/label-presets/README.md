# Capture real Gainscha label preset templates



The embedded `.sds` files under `src/PrinterInstall.Core/Gainscha/Templates/` must be **real exports** from the Seagull driver.



## What deploy validates



Deploy applies **two** Seagull stores:



| Store | Windows UI | Template file |

|-------|------------|---------------|

| Printing preferences | Properties → Preferences → Page Setup | `{preset}.sds` |

| Printing Defaults | Properties → Advanced → Printing Defaults → Page Setup | `{preset}-defaults.sds` |



Both are imported via `ssdal settings import` and validated by export (USER width × height in mm).



After deploy, confirm **both** dialogs show the correct USER size (e.g. 89 × 36 mm for Paciente).



To hide default stocks in the dropdown, delete `2 x 4`, `4 x 4`, and `4 x 6` manually once in printer preferences (Seagull does not remove them via `ssdal` import alone).



## One-time capture per preset



On a Windows PC with the Gainscha GA-2408T driver installed:



### 1. Preferences template (`{preset}.sds`)



1. Create or use a capture queue (for example `Etiquetadora`).

2. Open **Printer properties → Preferences → Page Setup → Label Paper**.

3. Delete every stock except one custom `USER` with the target dimensions.

4. Export:

   ```powershell

   .\scripts\Capture-GainschaLabelPreset.ps1 -PrinterName "Etiquetadora" -Preset Paciente -OutputDirectory C:\Temp -Target Preferences

   ```

5. Copy to `src/PrinterInstall.Core/Gainscha/Templates/<preset>.sds`.



### 2. Printing Defaults template (`{preset}-defaults.sds`)



1. Use the same queue (or a fresh one with driver defaults in both stores).

2. Open **Printer properties → Advanced → Printing Defaults → Page Setup → Label Paper**.

3. Configure **only** Printing Defaults (do not change Preferences).

4. Export:

   ```powershell

   .\scripts\Capture-GainschaLabelPreset.ps1 -PrinterName "Etiquetadora" -Preset Paciente -OutputDirectory C:\Temp -Target PrintingDefaults

   ```

5. Copy to `src/PrinterInstall.Core/Gainscha/Templates/<preset>-defaults.sds`.



6. Rebuild/publish and copy to the deployment share.



**Note:** The current `{preset}-defaults.sds` placeholders are copies of the preferences templates until real Printing Defaults captures are done on a Gainscha PC.



## Expected dimensions



| Preset   | USER size              |

|----------|------------------------|

| Paciente | 89 × 36 mm             |

| Matrix   | 50 × 30 mm             |

| Pulseira | 25 × 270 mm            |



## Remote fallback



If headless `ssdal import` of `{preset}-defaults.sds` does not validate, deploy retries with an interactive `DocumentProperties` sync when the deploy user has an active session on the target PC (30 s timeout). Otherwise deploy fails with a clear message to log in and redeploy.

