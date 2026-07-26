<#
.SYNOPSIS
    Déclare (ou retire) les tâches planifiées qui remettent les notifications locales (§4).

.DESCRIPTION
    Deux tâches, dans le Planificateur de tâches de l'utilisateur courant :

      Deuxieme Cerveau — rappels    quotidienne, 08:00    « --rappels »
      Deuxieme Cerveau — digest     dimanche,    09:00    « --digest »

    La quotidienne annonce les échéances du lendemain ; l'hebdomadaire force le point de la semaine
    et, une fois par mois, le rappel d'export (§5.7).

    Les deux se recouvrent volontairement — le dimanche, la quotidienne ferait déjà le digest. C'est
    le journal (rappels.json) qui rend le doublon impossible, et ce recouvrement achète une chose
    précise : avec -StartWhenAvailable, une machine éteinte le dimanche déclenche quand même le
    digest au rallumage, ce qu'un simple contrôle du jour de la semaine ne saurait pas rattraper.

    Aucun privilège administrateur : ce sont des tâches utilisateur. Elles ne tournent que session
    ouverte, ce qui est la seule façon d'afficher un toast.

    Si l'application est déjà ouverte quand une tâche se déclenche, le processus s'efface : c'est
    l'instance en place qui notifie, pour qu'un seul processus touche à local.db (D-019).

.PARAMETER Chemin
    Chemin de DeuxiemeCerveau.Windows.exe. Par défaut, la dernière compilation trouvée.

.PARAMETER Supprimer
    Retire les deux tâches au lieu de les déclarer.

.EXAMPLE
    .\PlanifierRappels.ps1
.EXAMPLE
    .\PlanifierRappels.ps1 -Chemin 'C:\Apps\DeuxiemeCerveau\DeuxiemeCerveau.Windows.exe'
.EXAMPLE
    .\PlanifierRappels.ps1 -Supprimer
#>
[CmdletBinding()]
param(
    [string] $Chemin,
    [switch] $Supprimer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TacheRappels = 'Deuxieme Cerveau - rappels'
$TacheDigest  = 'Deuxieme Cerveau - digest'

function Retirer([string] $nom) {
    if (Get-ScheduledTask -TaskName $nom -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $nom -Confirm:$false
        Write-Host "Retirée : $nom"
    }
    else {
        Write-Host "Absente  : $nom"
    }
}

if ($Supprimer) {
    Retirer $TacheRappels
    Retirer $TacheDigest
    return
}

if (-not $Chemin) {
    $racine = Join-Path $PSScriptRoot 'DeuxiemeCerveau.Windows\bin'
    $Chemin = Get-ChildItem -Path $racine -Filter 'DeuxiemeCerveau.Windows.exe' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $Chemin -or -not (Test-Path $Chemin)) {
    throw "Exécutable introuvable. Compilez l'application, ou passez -Chemin <...\DeuxiemeCerveau.Windows.exe>."
}

$Chemin = (Resolve-Path $Chemin).Path
Write-Host "Exécutable : $Chemin"

# Interactive : un toast n'existe que dans une session ouverte. Pas de -RunLevel Highest — les
# notifications sont refusées aux applications élevées.
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive

# StartWhenAvailable : rattrape un déclenchement manqué (machine éteinte). C'est ce qui sauve le
# digest d'une semaine où le dimanche s'est passé sans ordinateur.
# Batteries : un portable débranché doit notifier comme les autres.
$reglages = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10)

function Declarer([string] $nom, [string] $option, $declencheur, [string] $description) {
    $action = New-ScheduledTaskAction -Execute $Chemin -Argument $option
    Register-ScheduledTask `
        -TaskName $nom `
        -Action $action `
        -Trigger $declencheur `
        -Principal $principal `
        -Settings $reglages `
        -Description $description `
        -Force | Out-Null
    Write-Host "Déclarée : $nom"
}

Declarer $TacheRappels '--rappels' `
    (New-ScheduledTaskTrigger -Daily -At '08:00') `
    'Echeances et rendez-vous du lendemain (Deuxieme Cerveau, §4).'

Declarer $TacheDigest '--digest' `
    (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At '09:00') `
    'Point de la semaine et rappel mensuel d''export (Deuxieme Cerveau, §5.7).'

Write-Host ''
Write-Host 'Fait. Pour vérifier sans attendre :'
Write-Host "  Start-ScheduledTask -TaskName '$TacheDigest'"
Write-Host 'Pour retirer :'
Write-Host '  .\PlanifierRappels.ps1 -Supprimer'
