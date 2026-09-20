# Collects every string the mod puts in front of a player into one catalogue for translators:
# localization/strings.json - the string, the languages it exists in, and what it is for.
#
# The catalogue is generated, never edited by hand: items.jsonc and trader/trader.jsonc stay
# the single source of truth. Translate there, then run this again.
#
#   pwsh -File build/dump-strings.ps1

[CmdletBinding()]
param(
    [string] $Out = (Join-Path $PSScriptRoot '..\localization\strings.json')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

# The game ships these; a language without its own text falls back to "en".
$Order = @('en', 'ru', 'ch', 'es', 'po', 'ge', 'fr', 'jp', 'pl', 'tu', 'kr',
           'cz', 'es-mx', 'hu', 'it', 'ro', 'sk')
$LanguageNames = [ordered]@{
    'en' = 'English'; 'ru' = 'Russian'; 'ch' = 'Chinese (Simplified)'; 'es' = 'Spanish'
    'po' = 'Portuguese (Brazil)'; 'ge' = 'German'; 'fr' = 'French'; 'jp' = 'Japanese'
    'pl' = 'Polish'; 'tu' = 'Turkish'; 'kr' = 'Korean'; 'cz' = 'Czech'
    'es-mx' = 'Spanish (Latin America)'; 'hu' = 'Hungarian'; 'it' = 'Italian'
    'ro' = 'Romanian'; 'sk' = 'Slovak'
}

# ConvertFrom-Json chokes on the // comments the config files carry, so drop them first -
# character by character, because a "//" inside a string is not a comment.
function Remove-JsonComments([string] $text) {
    $out = [Text.StringBuilder]::new()
    $inString = $false
    $escaped = $false
    for ($i = 0; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if ($inString) {
            [void]$out.Append($c)
            if ($escaped) { $escaped = $false }
            elseif ($c -eq '\') { $escaped = $true }
            elseif ($c -eq '"') { $inString = $false }
            continue
        }

        if ($c -eq '"') { $inString = $true; [void]$out.Append($c); continue }
        if ($c -eq '/' -and $i + 1 -lt $text.Length -and $text[$i + 1] -eq '/') {
            while ($i -lt $text.Length -and $text[$i] -ne "`n") { $i++ }
            [void]$out.Append("`n")
            continue
        }

        [void]$out.Append($c)
    }

    return $out.ToString()
}

function Read-Jsonc([string] $path) {
    (Remove-JsonComments (Get-Content $path -Raw -Encoding utf8)) | ConvertFrom-Json -AsHashtable
}

# Languages first and in the order above, so two dumps of the same config compare cleanly.
function Sort-Locales([hashtable] $byLanguage) {
    $sorted = [ordered]@{}
    foreach ($language in $Order) {
        if ($byLanguage.ContainsKey($language)) { $sorted[$language] = $byLanguage[$language] }
    }
    foreach ($language in ($byLanguage.Keys | Sort-Object)) {
        if (-not $sorted.Contains($language)) { $sorted[$language] = $byLanguage[$language] }
    }

    return $sorted
}

$strings = [Collections.Generic.List[object]]::new()
function Add-String($id, $context, $locales, $limit, $placeholders) {
    $entry = [ordered]@{ id = $id; context = $context }
    if ($limit) { $entry.maxLength = $limit }
    if ($placeholders) { $entry.placeholders = $placeholders }
    $entry.locales = Sort-Locales $locales
    $strings.Add($entry)
}

# Pulls one field out of every language of an item's "locales" block.
function Field([hashtable] $locales, [string] $field) {
    $byLanguage = @{}
    foreach ($language in $locales.Keys) {
        $value = $locales[$language][$field]
        if ($value) { $byLanguage[$language] = $value }
    }

    return $byLanguage
}

$items = Read-Jsonc (Join-Path $root 'server\mod-files\items.jsonc')
$trader = Read-Jsonc (Join-Path $root 'server\mod-files\trader\trader.jsonc')

Add-String 'ui.pouchSlot' `
    ('Label over an empty pouch cell in the inspect window. The game writes its own cell labels ' +
     'in upper case and keeps them short (MOD_MAGAZINE reads "MAGAZINE" / "МАГАЗИН").') `
    $items.pouchSlotName 11 $null

foreach ($key in $items.colors.Keys) {
    Add-String "color.$key" `
        ("Colour name of a rig or a pouch, put where its name says {color}. The key '$key' is part " +
         'of the item id and of the bundle name and never changes; only the text does.') `
        $items.colors[$key] $null $null
}

Add-String 'vest.description' `
    ('Description shared by every rig, in the inspect window and at the trader. The rigs differ ' +
     'in model and protection, which the name and the stats already say; this says the one thing ' +
     'a player cannot see - the rig has no storage of its own.') `
    $items.vestDescription $null $null

Add-String 'pouch.description' `
    ('Description shared by every pouch. The name says what it is and the grid says how much it ' +
     'holds, so this says only that it goes on a modular rig rather than in a bag.') `
    $items.pouchDescription $null $null

foreach ($vest in $items.vests) {
    Add-String "vest.$($vest.key).name" `
        ('Full name of a rig, in the inspect window, at the trader and on the flea market. One ' +
         'carrier comes in five colours, so {color} stands where the colour goes. The model ' +
         'designation stays as the game writes it; what is translated is the kind of item and, ' +
         'for the IOTV, the kit.') `
        (Field $vest.locales 'name') $null @('{color}')

    Add-String "vest.$($vest.key).shortName" `
        ('Short name of a rig, drawn in the item cell in the stash. Built from the donor item''s ' +
         'own short name in the game''s locales plus an " M" marker, so the abbreviation is the ' +
         'one the player already reads on the vanilla item and the modular version is still ' +
         'told apart from it. Vanilla rigs run 3-11 characters.') `
        (Field $vest.locales 'shortName') 13 $null
}

foreach ($pouch in $items.pouches) {
    Add-String "pouch.$($pouch.key).name" `
        'Full name of a pouch, in the inspect window, at the trader and on the flea market.' `
        (Field $pouch.locales 'name') $null @('{color}')

    Add-String "pouch.$($pouch.key).shortName" `
        ('Short name of a pouch, drawn in the item cell. Pouches are 1 or 2 cells wide, so this ' +
         'has to stay short; it carries no colour, exactly as the game''s own colour variants ' +
         'share one short name.') `
        (Field $pouch.locales 'shortName') 12 $null
}

$traderContext = [ordered]@{
    nickname    = 'The handle players call the trader by, on the trader list and the trader screen. The game translates its own (Ragman -> Барахольщик).'
    fullName    = 'Surname, first name and patronymic on the trader card, the way the game writes them ("Романенко Павел Егорович").'
    firstName   = 'First name AND patronymic ("Павел Егорович"), not the first name alone - that is how the game fills this field.'
    location    = 'Where the trader sits, on the trader card. The game names a place in Tarkov ("Посёлок", "Химкомбинат"), not a kind of business.'
    description = 'Who the trader is, on the trader card. The game''s own run to two sentences: what he did before the conflict, what he deals in now.'
}
foreach ($field in $traderContext.Keys) {
    $byLanguage = @{}
    foreach ($language in $trader.locales.Keys) {
        $value = $trader.locales[$language][$field]
        if ($value) { $byLanguage[$language] = $value }
    }

    Add-String "trader.$($trader.key).$field" $traderContext[$field] $byLanguage $null $null
}

$catalogue = [ordered]@{
    mod       = 'Modular Vests'
    generated = (Get-Date -Format 'yyyy-MM-dd')
    readme    = @(
        'Generated by build/dump-strings.ps1 - do not edit. The strings live in',
        'server/mod-files/items.jsonc and server/mod-files/trader/trader.jsonc; translate there',
        'and run the script again.',
        'A language the game ships but a string does not list falls back to "en".',
        'maxLength is what fits the UI, not a hard limit - longer text is drawn clipped.'
    )
    languages = $LanguageNames
    strings   = $strings
}

$outPath = [IO.Path]::GetFullPath($Out)
$outDir = Split-Path $outPath -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$json = $catalogue | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($outPath, $json + "`n", [Text.UTF8Encoding]::new($false))

$languages = $strings | ForEach-Object { $_.locales.Keys } | Sort-Object -Unique
Write-Host "$($strings.Count) strings in $($languages.Count) languages -> $outPath"
