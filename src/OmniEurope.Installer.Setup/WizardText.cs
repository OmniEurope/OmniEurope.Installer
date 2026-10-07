namespace OmniEurope.Installer.Setup;

/// <summary>
/// The wizard's French and English texts, with the product name as {0}. <see cref="French"/> follows
/// the language chosen on the first page.
/// </summary>
internal sealed class WizardText
{
    private readonly string _product;

    public WizardText(string product)
    {
        _product = product;
    }

    public bool French { get; set; } = true;

    public string Title => T("Installation de {0}", "{0} Setup");
    public string LangHeader => T("Bienvenue", "Welcome");
    public string LangInfo => T("Sélectionnez la langue d’installation, puis cliquez sur Suivant.", "Select the installation language, then click Next.");
    public string LangLabel => T("Langue :", "Language:");
    public string LicenseHeader => T("Contrat de licence", "License Agreement");
    public string LicenseInfo => T("Veuillez lire et accepter le contrat de licence.", "Please read and accept the license agreement.");
    public string Accept => T("J’accepte les termes du contrat de licence", "I accept the terms in the license agreement");
    public string LicenseMissing => T("Le texte du contrat de licence n’a pas pu être chargé.", "The license agreement text could not be loaded.");
    public string DirHeader => T("Dossier d’installation", "Installation Folder");
    public string DirInfo => T("{0} sera installé dans le dossier suivant. Cliquez sur Parcourir pour changer.", "{0} will be installed in the following folder. Click Browse to change.");
    public string DirLabel => T("Dossier :", "Folder:");
    public string Browse => T("Parcourir…", "Browse…");
    public string ProgressHeader => T("Installation en cours", "Installing");
    public string ProgressInfo => T("Veuillez patienter…", "Please wait…");
    public string CompleteHeader => T("Installation terminée", "Setup Complete");
    public string CompleteInfo => T("{0} a été installé avec succès.", "{0} has been installed successfully.");
    public string Launch => T("Lancer {0}", "Launch {0}");
    public string ViewLog => T("Voir le journal d’installation", "View installation log");
    public string Back => T("< Précédent", "< Back");
    public string Next => T("Suivant >", "Next >");
    public string Install => T("Installer", "Install");
    public string Cancel => T("Annuler", "Cancel");
    public string Close => T("Fermer", "Close");
    public string MaintenanceHeader => T("{0} est déjà installé", "{0} is already installed");
    public string MaintenanceInfo => T("Choisissez une action :", "Choose an action:");
    public string Repair => T("Réparer", "Repair");
    public string RepairInfo => T("Réinstalle tous les fichiers de l’application.", "Reinstalls all application files.");
    public string Update => T("Mettre à jour", "Update");
    public string Uninstall => T("Désinstaller", "Uninstall");
    public string UninstallInfo => T("Supprime {0} de votre ordinateur.", "Removes {0} from your computer.");
    public string Repairing => T("Réparation en cours", "Repairing");
    public string Updating => T("Mise à jour en cours", "Updating");
    public string Uninstalling => T("Désinstallation en cours", "Uninstalling");
    public string RepairComplete => T("Réparation terminée", "Repair Complete");
    public string RepairCompleteInfo => T("{0} a été réparé avec succès.", "{0} has been repaired successfully.");
    public string UpdateComplete => T("Mise à jour terminée", "Update Complete");
    public string UpdateCompleteInfo => T("{0} a été mis à jour avec succès.", "{0} has been updated successfully.");
    public string UninstallComplete => T("Désinstallation terminée", "Uninstall Complete");
    public string UninstallCompleteInfo => T("{0} a été désinstallé avec succès.", "{0} has been uninstalled successfully.");

    public string RestartRequired => T("Redémarrez l’ordinateur pour terminer : des fichiers étaient encore utilisés.", "Restart the computer to finish: some files were still in use.");

    public string MaintenanceHeaderUpdate(string installedVersion) => string.Format(Pick("{0} {1} est installé", "{0} {1} is installed"), _product, installedVersion);

    public string UpdateInfo(string version) => string.Format(Pick("Met à jour vers la version {1}.", "Updates to version {1}."), _product, version);

    public string SameVersionInfo(string version) => string.Format(Pick("Remplace l’installation existante par cette version {1}.", "Replaces the existing installation with this {1} build."), _product, version);

    public string DowngradeBlocked(string version, string installedVersion) => string.Format(
        Pick("Cette version ({1}) est plus ancienne que la version installée ({2}). L’installation est impossible.",
          "This version ({1}) is older than the installed version ({2}). Installation is not possible."),
        _product, version, installedVersion);

    public string Failed(uint code) => string.Format(
        Pick("Échec de l’opération (code {1}). Le journal d’installation donne le détail.", "Operation failed (code {1}). The installation log has the details."),
        _product, code);

    private string Pick(string french, string english) => French ? french : english;

    private string T(string french, string english) => string.Format(Pick(french, english), _product);
}
