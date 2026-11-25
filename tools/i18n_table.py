"""Single translation table for Marea: key -> (it, en, es, fr, de).

Run `python tools/i18n_table.py` to regenerate src/Marea.Core/i18n/*.json.
Placeholders {0}, {1}... must be identical in every language (a test checks it).
"""
import json
import os

LANGS = ["it", "en", "es", "fr", "de"]

T = {
# --- time and durations
"ago.now": ("adesso", "just now", "ahora", "à l'instant", "gerade eben"),
"ago.hours": ("{0} ore fa", "{0} hours ago", "hace {0} horas", "il y a {0} heures", "vor {0} Stunden"),
"ago.yesterday": ("ieri", "yesterday", "ayer", "hier", "gestern"),
"ago.days": ("{0} giorni fa", "{0} days ago", "hace {0} días", "il y a {0} jours", "vor {0} Tagen"),
"dur.instant": ("istantaneo", "instant", "instantáneo", "instantané", "sofort"),
"app.tagline": ("spazio che va e torna", "space that ebbs and flows", "espacio que va y vuelve", "l'espace qui va et revient", "Speicher, der geht und kommt"),
# --- classes and states
"class.duplicate": ("Duplicato", "Duplicate", "Duplicado", "Doublon", "Duplikat"),
"class.derivable": ("Derivabile", "Derivable", "Derivable", "Dérivable", "Ableitbar"),
"class.reobtainable": ("Riottenibile", "Re-obtainable", "Recuperable", "Récupérable", "Wiederbeschaffbar"),
"class.unique": ("Unico", "Unique", "Único", "Unique", "Einzigartig"),
"res.local": ("Sul disco", "On disk", "En el disco", "Sur le disque", "Auf der Platte"),
"res.evaporated": ("Evaporato", "Evaporated", "Evaporado", "Évaporé", "Verdunstet"),
"res.changed": ("Modificato", "Changed", "Modificado", "Modifié", "Geändert"),
"res.missing": ("Mancante", "Missing", "Ausente", "Manquant", "Fehlt"),
# --- navigation and shell
"nav.overview": ("Panoramica", "Overview", "Resumen", "Vue d'ensemble", "Übersicht"),
"nav.files": ("File", "Files", "Archivos", "Fichiers", "Dateien"),
"nav.folders": ("Cartelle gestite", "Managed folders", "Carpetas gestionadas", "Dossiers gérés", "Verwaltete Ordner"),
"nav.activity": ("Attività", "Activity", "Actividad", "Activité", "Aktivität"),
"nav.settings": ("Impostazioni", "Settings", "Ajustes", "Paramètres", "Einstellungen"),
"prov.active": ("Servizio attivo", "Service running", "Servicio activo", "Service actif", "Dienst aktiv"),
"prov.active_detail": ("I file evaporati tornano da soli quando un programma li legge.", "Evaporated files come back on their own when a program reads them.", "Los archivos evaporados vuelven solos cuando un programa los lee.", "Les fichiers évaporés reviennent seuls quand un programme les lit.", "Verdunstete Dateien kommen von selbst zurück, wenn ein Programm sie liest."),
"prov.elsewhere": ("Servizio in un'altra istanza", "Service in another instance", "Servicio en otra instancia", "Service dans une autre instance", "Dienst in einer anderen Instanz"),
"prov.elsewhere_detail": ("Un'altra istanza (per esempio mareactl agent) serve i file evaporati.", "Another instance (e.g. mareactl agent) serves the evaporated files.", "Otra instancia (p. ej. mareactl agent) sirve los archivos evaporados.", "Une autre instance (p. ex. mareactl agent) sert les fichiers évaporés.", "Eine andere Instanz (z. B. mareactl agent) liefert die verdunsteten Dateien."),
"op.cancel": ("Annulla", "Cancel", "Cancelar", "Annuler", "Abbrechen"),
"op.busy": ("C'è già un'operazione in corso.", "Another operation is already running.", "Ya hay una operación en curso.", "Une opération est déjà en cours.", "Es läuft bereits ein Vorgang."),
"op.scan": ("Scansione e ricerca delle fonti", "Scanning and looking for sources", "Escaneo y búsqueda de fuentes", "Analyse et recherche des sources", "Scan und Quellensuche"),
"op.evaporate": ("Evaporazione", "Evaporating", "Evaporando", "Évaporation", "Verdunsten"),
"op.bring_back": ("Riporto sul disco", "Bringing back to disk", "Devolviendo al disco", "Retour sur le disque", "Zurück auf die Platte"),
"op.replicate": ("Replica sul mirror", "Replicating to mirror", "Replicando en el espejo", "Réplication sur le miroir", "Auf Spiegel replizieren"),
"op.remove_root": ("Rilascio della cartella", "Releasing the folder", "Liberando la carpeta", "Libération du dossier", "Ordner wird freigegeben"),
"op.restore_all": ("Ripristino completo", "Full restore", "Restauración completa", "Restauration complète", "Vollständige Wiederherstellung"),
"hyd.bar": ("Torna sul disco: {0} da {1} — {2} di {3}", "Coming back: {0} from {1} — {2} of {3}", "Volviendo: {0} desde {1} — {2} de {3}", "Retour : {0} depuis {1} — {2} sur {3}", "Kommt zurück: {0} von {1} — {2} von {3}"),
# --- overview
"ov.title": ("Panoramica", "Overview", "Resumen", "Vue d'ensemble", "Übersicht"),
"ov.subtitle": ("Marea sa da dove si può riavere ogni byte. Libera solo ciò che ha una fonte verificata e lo riporta quando un programma lo apre.", "Marea knows where every byte can be obtained again. It frees only what has a verified source and brings it back when a program opens it.", "Marea sabe de dónde se puede recuperar cada byte. Libera solo lo que tiene una fuente verificada y lo devuelve cuando un programa lo abre.", "Marea sait d'où chaque octet peut être récupéré. Elle ne libère que ce qui a une source vérifiée et le ramène quand un programme l'ouvre.", "Marea weiß, woher sich jedes Byte wieder beschaffen lässt. Es gibt nur frei, was eine geprüfte Quelle hat, und holt es zurück, sobald ein Programm es öffnet."),
"ov.empty_title": ("Scegli la prima cartella da gestire", "Choose the first folder to manage", "Elige la primera carpeta que gestionar", "Choisissez le premier dossier à gérer", "Wähle den ersten Ordner aus"),
"ov.empty_text": ("Una cartella di modelli, per esempio …\\checkpoints o …\\ComfyUI\\models. Marea non tocca nulla finché non confermi un piano.", "A model folder, for example …\\checkpoints or …\\ComfyUI\\models. Marea touches nothing until you confirm a plan.", "Una carpeta de modelos, por ejemplo …\\checkpoints o …\\ComfyUI\\models. Marea no toca nada hasta que confirmes un plan.", "Un dossier de modèles, par exemple …\\checkpoints ou …\\ComfyUI\\models. Marea ne touche à rien tant que vous n'avez pas confirmé un plan.", "Ein Modellordner, zum Beispiel …\\checkpoints oder …\\ComfyUI\\models. Marea ändert nichts, bevor du einen Plan bestätigst."),
"ov.add_folder": ("Aggiungi cartella…", "Add folder…", "Añadir carpeta…", "Ajouter un dossier…", "Ordner hinzufügen…"),
"ov.volume_title": ("Disco {0} · {1} liberi", "Drive {0} · {1} free", "Disco {0} · {1} libres", "Disque {0} · {1} libres", "Laufwerk {0} · {1} frei"),
"ov.volume_detail": ("{0} usati su {1}. La marea tiene almeno {2} liberi.", "{0} used of {1}. The tide keeps at least {2} free.", "{0} usados de {1}. La marea mantiene al menos {2} libres.", "{0} utilisés sur {1}. La marée garde au moins {2} libres.", "{0} von {1} belegt. Die Flut hält mindestens {2} frei."),
"ov.auto_on": (" La marea automatica è attiva.", " Automatic tide is on.", " La marea automática está activada.", " La marée automatique est activée.", " Die automatische Flut ist an."),
"ov.auto_off": (" La marea automatica è spenta.", " Automatic tide is off.", " La marea automática está desactivada.", " La marée automatique est désactivée.", " Die automatische Flut ist aus."),
"ov.below": ("sotto la soglia di {0}", "{0} below target", "{0} por debajo del umbral", "{0} sous le seuil", "{0} unter dem Ziel"),
"ov.above": ("sopra la soglia", "above target", "por encima del umbral", "au-dessus du seuil", "über dem Ziel"),
"ov.allowed": ("Liberabile ora, in sicurezza", "Safely freeable now", "Liberable ahora, con seguridad", "Libérable maintenant, en sécurité", "Jetzt sicher freigebbar"),
"ov.evaporated": ("Già evaporato", "Already evaporated", "Ya evaporado", "Déjà évaporé", "Bereits verdunstet"),
"ov.unique": ("Unico: resta sempre", "Unique: always stays", "Único: siempre se queda", "Unique : reste toujours", "Einzigartig: bleibt immer"),
"ov.total": ("Nel registro", "In the ledger", "En el registro", "Dans le registre", "Im Register"),
"ov.breakdown_title": ("Da dove si riavrebbe ogni byte", "Where every byte would come back from", "De dónde volvería cada byte", "D'où reviendrait chaque octet", "Woher jedes Byte zurückkäme"),
"ov.breakdown_help": ("Duplicato: c'è una copia identica sul disco. Derivabile: si ricalcola bit a bit da un altro file (per esempio bf16 da fp32). Riottenibile: identico a un file pubblico o alla copia sul mirror. Unico: nessuna fonte, non si tocca.", "Duplicate: an identical copy is on disk. Derivable: rebuilt bit for bit from another file (e.g. bf16 from fp32). Re-obtainable: identical to a public file or to the mirror copy. Unique: no source, never touched.", "Duplicado: hay una copia idéntica en el disco. Derivable: se recalcula bit a bit desde otro archivo (p. ej. bf16 desde fp32). Recuperable: idéntico a un archivo público o a la copia del espejo. Único: sin fuente, no se toca.", "Doublon : une copie identique est sur le disque. Dérivable : recalculé bit à bit depuis un autre fichier (p. ex. bf16 depuis fp32). Récupérable : identique à un fichier public ou à la copie du miroir. Unique : aucune source, jamais touché.", "Duplikat: eine identische Kopie liegt auf der Platte. Ableitbar: wird Bit für Bit aus einer anderen Datei berechnet (z. B. bf16 aus fp32). Wiederbeschaffbar: identisch mit einer öffentlichen Datei oder der Spiegelkopie. Einzigartig: keine Quelle, wird nie angefasst."),
"ov.free": ("Libera spazio…", "Free up space…", "Liberar espacio…", "Libérer de l'espace…", "Speicher freigeben…"),
"ov.scan": ("Scansiona e cerca fonti", "Scan and find sources", "Escanear y buscar fuentes", "Analyser et chercher les sources", "Scannen und Quellen suchen"),
"ov.no_scan": ("Nessuna scansione ancora.", "No scan yet.", "Aún no hay escaneo.", "Aucune analyse pour l'instant.", "Noch kein Scan."),
"ov.last_scan": ("{0} file nel registro. Ultima impronta: {1}.", "{0} files in the ledger. Last fingerprint: {1}.", "{0} archivos en el registro. Última huella: {1}.", "{0} fichiers dans le registre. Dernière empreinte : {1}.", "{0} Dateien im Register. Letzter Fingerabdruck: {1}."),
"ov.recent": ("Ultime attività", "Recent activity", "Actividad reciente", "Activité récente", "Letzte Aktivität"),
# --- quick-free wizard
"ov.quick": ("Libera spazio in 3 clic", "Free up space in 3 clicks", "Liberar espacio en 3 clics", "Libérer de l'espace en 3 clics", "Speicher in 3 Klicks freigeben"),
"ov.undo_last": ("Annulla l'ultima liberazione ({0}, {1} file)", "Undo last cleanup ({0}, {1} files)", "Deshacer la última liberación ({0}, {1} archivos)", "Annuler la dernière libération ({0}, {1} fichiers)", "Letzte Freigabe rückgängig ({0}, {1} Dateien)"),
"op.undo": ("Annullamento", "Undoing", "Deshaciendo", "Annulation", "Rückgängig machen"),
"tray.quick": ("Libera spazio in 3 clic…", "Free up space in 3 clicks…", "Liberar espacio en 3 clics…", "Libérer de l'espace en 3 clics…", "Speicher in 3 Klicks freigeben…"),
"qf.title": ("Libera spazio in 3 clic", "Free up space in 3 clicks", "Liberar espacio en 3 clics", "Libérer de l'espace en 3 clics", "Speicher in 3 Klicks freigeben"),
"qf.subtitle": ("Scegli quanto e come. Marea tocca solo file con una fonte verificata e puoi annullare tutto alla fine.", "Choose how much and how. Marea only touches files with a verified source, and you can undo everything at the end.", "Elige cuánto y cómo. Marea solo toca archivos con una fuente verificada y al final puedes deshacerlo todo.", "Choisissez combien et comment. Marea ne touche qu'aux fichiers ayant une source vérifiée, et vous pouvez tout annuler à la fin.", "Wähle wie viel und wie. Marea fasst nur Dateien mit geprüfter Quelle an, und am Ende kannst du alles rückgängig machen."),
"qf.how_much": ("1. Quanto spazio ti serve?", "1. How much space do you need?", "1. ¿Cuánto espacio necesitas?", "1. De combien d'espace avez-vous besoin ?", "1. Wie viel Speicher brauchst du?"),
"qf.to_target": ("Fino alla soglia", "Up to the target", "Hasta el umbral", "Jusqu'au seuil", "Bis zum Ziel"),
"qf.all_safe": ("Tutto quello che è sicuro", "Everything that is safe", "Todo lo que es seguro", "Tout ce qui est sûr", "Alles, was sicher ist"),
"qf.all_note": ("il massimo possibile, senza rischi", "as much as possible, without risk", "lo máximo posible, sin riesgo", "le maximum possible, sans risque", "so viel wie möglich, ohne Risiko"),
"qf.above_target": ("sei già sopra la soglia", "you are already above the target", "ya estás por encima del umbral", "vous êtes déjà au-dessus du seuil", "du bist bereits über dem Ziel"),
"qf.available": ("disponibili", "available", "disponibles", "disponibles", "verfügbar"),
"qf.only": ("solo {0} disponibili", "only {0} available", "solo {0} disponibles", "seulement {0} disponibles", "nur {0} verfügbar"),
"qf.how_back": ("2. Come devono tornare i file?", "2. How must files come back?", "2. ¿Cómo deben volver los archivos?", "2. Comment les fichiers doivent-ils revenir ?", "2. Wie sollen die Dateien zurückkommen?"),
"qf.instant": ("Solo ritorno istantaneo (consigliato) · {0}", "Instant return only (recommended) · {0}", "Solo regreso instantáneo (recomendado) · {0}", "Retour instantané uniquement (recommandé) · {0}", "Nur sofortige Rückkehr (empfohlen) · {0}"),
"qf.instant_help": ("Duplicati e file ricalcolabili: tornano in pochi secondi, anche senza internet.", "Duplicates and recomputable files: they come back in seconds, even offline.", "Duplicados y archivos recalculables: vuelven en segundos, incluso sin internet.", "Doublons et fichiers recalculables : ils reviennent en quelques secondes, même hors ligne.", "Duplikate und neu berechenbare Dateien: kommen in Sekunden zurück, auch offline."),
"qf.download": ("Anche dal NAS o da internet · {0}", "Also from NAS or internet · {0}", "También desde el NAS o internet · {0}", "Aussi depuis le NAS ou internet · {0}", "Auch vom NAS oder aus dem Internet · {0}"),
"qf.download_help": ("Libera di più: i file pubblici o sul NAS tornano con un download, e il tempo dipende dalla rete.", "Frees more: public or NAS files come back by download, so timing depends on your network.", "Libera más: los archivos públicos o del NAS vuelven por descarga y el tiempo depende de la red.", "Libère davantage : les fichiers publics ou du NAS reviennent par téléchargement, le délai dépend du réseau.", "Gibt mehr frei: öffentliche oder NAS-Dateien kommen per Download zurück, die Dauer hängt vom Netz ab."),
"qf.summary": ("Libererai {0} da {1} file. Nessun file unico viene toccato: tornano da soli quando li apri, finché Marea è attiva. Ritorno più lento: {2}.", "You will free {0} from {1} files. No unique file is touched: they come back on their own when opened, while Marea is running. Slowest return: {2}.", "Liberarás {0} de {1} archivos. No se toca ningún archivo único: vuelven solos al abrirlos, mientras Marea esté activa. Regreso más lento: {2}.", "Vous libérerez {0} depuis {1} fichiers. Aucun fichier unique n'est touché : ils reviennent seuls à l'ouverture, tant que Marea est active. Retour le plus lent : {2}.", "Du gibst {0} aus {1} Dateien frei. Keine einzigartige Datei wird angefasst: Sie kommen beim Öffnen von selbst zurück, solange Marea läuft. Langsamste Rückkehr: {2}."),
"qf.less_than_asked": ("È meno dei {0} richiesti: di più non si può liberare in sicurezza.", "That is less than the {0} you asked for: more cannot be freed safely.", "Es menos de los {0} pedidos: no se puede liberar más con seguridad.", "C'est moins que les {0} demandés : impossible d'en libérer plus en sécurité.", "Das ist weniger als die gewünschten {0}: mehr lässt sich nicht sicher freigeben."),
"qf.nothing": ("Niente da liberare in sicurezza adesso. Prova una nuova scansione o aggiungi una cartella di modelli.", "Nothing can be freed safely right now. Try a new scan or add a model folder.", "Ahora no hay nada que liberar con seguridad. Prueba un nuevo escaneo o añade una carpeta de modelos.", "Rien à libérer en sécurité pour l'instant. Relancez une analyse ou ajoutez un dossier de modèles.", "Gerade lässt sich nichts sicher freigeben. Starte einen neuen Scan oder füge einen Modellordner hinzu."),
"qf.go": ("3. Libera ora", "3. Free up now", "3. Liberar ahora", "3. Libérer maintenant", "3. Jetzt freigeben"),
"qf.working": ("Libero spazio…", "Freeing space…", "Liberando espacio…", "Libération en cours…", "Speicher wird freigegeben…"),
"qf.done_title": ("Fatto: liberati {0}", "Done: {0} freed", "Hecho: {0} liberados", "Terminé : {0} libérés", "Fertig: {0} freigegeben"),
"qf.done_text": ("{0} file sono evaporati e torneranno da soli quando un programma li apre. Se qualcosa non ti convince, annulla con un clic.", "{0} files evaporated and will come back on their own when a program opens them. If anything looks wrong, undo with one click.", "{0} archivos se evaporaron y volverán solos cuando un programa los abra. Si algo no te convence, deshazlo con un clic.", "{0} fichiers se sont évaporés et reviendront seuls quand un programme les ouvrira. Si quelque chose vous inquiète, annulez en un clic.", "{0} Dateien sind verdunstet und kommen von selbst zurück, wenn ein Programm sie öffnet. Wenn dir etwas nicht passt, mach es mit einem Klick rückgängig."),
"qf.done_kept": ("{0} file sono rimasti sul disco per sicurezza:", "{0} files stayed on disk for safety:", "{0} archivos se quedaron en el disco por seguridad:", "{0} fichiers sont restés sur le disque par sécurité :", "{0} Dateien sind aus Sicherheitsgründen geblieben:"),
"qf.undo": ("Annulla questa operazione", "Undo this cleanup", "Deshacer esta operación", "Annuler cette opération", "Diesen Vorgang rückgängig machen"),
"qf.undoing": ("Riporto i file sul disco…", "Bringing the files back…", "Devolviendo los archivos…", "Retour des fichiers…", "Dateien werden zurückgeholt…"),
"qf.undone": ("Annullato: tutti i file di questa operazione sono di nuovo sul disco.", "Undone: every file of this cleanup is back on disk.", "Deshecho: todos los archivos de esta operación vuelven a estar en el disco.", "Annulé : tous les fichiers de cette opération sont de retour sur le disque.", "Rückgängig gemacht: alle Dateien dieses Vorgangs sind wieder auf der Platte."),
"qf.close": ("Chiudi", "Close", "Cerrar", "Fermer", "Schließen"),
"j.batch_undone": ("liberazione annullata", "cleanup undone", "liberación deshecha", "libération annulée", "Freigabe rückgängig gemacht"),
"eng.batch_detail": ("{0} file riportati sul disco", "{0} files brought back", "{0} archivos devueltos", "{0} fichiers ramenés", "{0} Dateien zurückgeholt"),
"cli.undo_hint": ("Per annullare: mareactl undo", "To undo: mareactl undo", "Para deshacer: mareactl undo", "Pour annuler : mareactl undo", "Rückgängig: mareactl undo"),
"cli.nothing_to_undo": ("Niente da annullare.", "Nothing to undo.", "Nada que deshacer.", "Rien à annuler.", "Nichts rückgängig zu machen."),
"cli.undone": ("Annullato: {0} file ({1}) di nuovo sul disco.", "Undone: {0} files ({1}) back on disk.", "Deshecho: {0} archivos ({1}) de nuevo en el disco.", "Annulé : {0} fichiers ({1}) de retour sur le disque.", "Rückgängig: {0} Dateien ({1}) wieder auf der Platte."),
# --- files
"files.title": ("File", "Files", "Archivos", "Fichiers", "Dateien"),
"files.subtitle": ("Ogni file con la sua prova di provenienza. Seleziona per i dettagli.", "Every file with its proof of provenance. Select one for details.", "Cada archivo con su prueba de procedencia. Selecciona uno para ver detalles.", "Chaque fichier avec sa preuve de provenance. Sélectionnez-en un pour les détails.", "Jede Datei mit ihrem Herkunftsnachweis. Für Details eine auswählen."),
"files.filter_all": ("Tutti i file", "All files", "Todos los archivos", "Tous les fichiers", "Alle Dateien"),
"files.evaporate": ("Evapora…", "Evaporate…", "Evaporar…", "Évaporer…", "Verdunsten…"),
"files.bring_back": ("Riporta sul disco", "Bring back to disk", "Devolver al disco", "Ramener sur le disque", "Zurückholen"),
"files.replicate": ("Replica sul mirror", "Replicate to mirror", "Replicar en el espejo", "Répliquer sur le miroir", "Auf Spiegel replizieren"),
"files.select_hint": ("Seleziona un file per vedere da dove si riavrebbe e perché resta o può evaporare.", "Select a file to see where it would come back from and why it stays or may evaporate.", "Selecciona un archivo para ver de dónde volvería y por qué se queda o puede evaporarse.", "Sélectionnez un fichier pour voir d'où il reviendrait et pourquoi il reste ou peut s'évaporer.", "Wähle eine Datei, um zu sehen, woher sie zurückkäme und warum sie bleibt oder verdunsten darf."),
"files.selected": ("{0} file selezionati · {1}", "{0} files selected · {1}", "{0} archivos seleccionados · {1}", "{0} fichiers sélectionnés · {1}", "{0} Dateien ausgewählt · {1}"),
"files.source_line": ("Fonte: {0}", "Source: {0}", "Fuente: {0}", "Source : {0}", "Quelle: {0}"),
"files.is_evaporated": ("Evaporato {0}. Torna da solo quando un programma lo apre.", "Evaporated {0}. Comes back on its own when a program opens it.", "Evaporado {0}. Vuelve solo cuando un programa lo abre.", "Évaporé {0}. Revient seul quand un programme l'ouvre.", "Verdunstet {0}. Kommt von selbst zurück, wenn ein Programm sie öffnet."),
"files.can_go": ("Può evaporare: tornerebbe in circa {0}.", "May evaporate: it would come back in about {0}.", "Puede evaporarse: volvería en unos {0}.", "Peut s'évaporer : il reviendrait en environ {0}.", "Darf verdunsten: käme in etwa {0} zurück."),
"files.why_stays": ("Resta sul disco perché:", "It stays on disk because:", "Se queda en el disco porque:", "Il reste sur le disque car :", "Bleibt auf der Platte, weil:"),
# --- folders
"folders.title": ("Cartelle gestite", "Managed folders", "Carpetas gestionadas", "Dossiers gérés", "Verwaltete Ordner"),
"folders.subtitle": ("Marea lavora solo dentro queste cartelle. Il resto del disco non viene mai toccato.", "Marea works only inside these folders. The rest of the disk is never touched.", "Marea solo trabaja dentro de estas carpetas. El resto del disco nunca se toca.", "Marea ne travaille que dans ces dossiers. Le reste du disque n'est jamais touché.", "Marea arbeitet nur in diesen Ordnern. Der Rest der Platte wird nie angefasst."),
"folders.add": ("Aggiungi cartella…", "Add folder…", "Añadir carpeta…", "Ajouter un dossier…", "Ordner hinzufügen…"),
"folders.find": ("Cerca cartelle di modelli", "Find model folders", "Buscar carpetas de modelos", "Chercher des dossiers de modèles", "Modellordner suchen"),
"folders.remove": ("Smetti di gestire", "Stop managing", "Dejar de gestionar", "Ne plus gérer", "Nicht mehr verwalten"),
"folders.summary": ("{0} file · {1} · {2} evaporati", "{0} files · {1} · {2} evaporated", "{0} archivos · {1} · {2} evaporados", "{0} fichiers · {1} · {2} évaporés", "{0} Dateien · {1} · {2} verdunstet"),
"folders.pick": ("Scegli una cartella di modelli", "Choose a model folder", "Elige una carpeta de modelos", "Choisissez un dossier de modèles", "Modellordner wählen"),
"folders.refused": ("Questa cartella non può essere gestita:\n{0}", "This folder cannot be managed:\n{0}", "Esta carpeta no se puede gestionar:\n{0}", "Ce dossier ne peut pas être géré :\n{0}", "Dieser Ordner kann nicht verwaltet werden:\n{0}"),
"folders.confirm_remove": ("Tutti i file di\n{0}\ntorneranno normali e Marea smetterà di gestire la cartella. Continuare?", "All files in\n{0}\nwill become ordinary again and Marea will stop managing the folder. Continue?", "Todos los archivos de\n{0}\nvolverán a ser normales y Marea dejará de gestionar la carpeta. ¿Continuar?", "Tous les fichiers de\n{0}\nredeviendront normaux et Marea cessera de gérer le dossier. Continuer ?", "Alle Dateien in\n{0}\nwerden wieder normal und Marea verwaltet den Ordner nicht mehr. Fortfahren?"),
# --- activity
"journal.title": ("Attività", "Activity", "Actividad", "Activité", "Aktivität"),
"journal.subtitle": ("Tutto ciò che Marea ha fatto, in ordine. Il registro non si cancella.", "Everything Marea did, in order. The journal is never erased.", "Todo lo que hizo Marea, en orden. El registro nunca se borra.", "Tout ce que Marea a fait, dans l'ordre. Le journal n'est jamais effacé.", "Alles, was Marea getan hat, der Reihe nach. Das Protokoll wird nie gelöscht."),
"col.name": ("Nome", "Name", "Nombre", "Nom", "Name"),
"col.folder": ("Cartella", "Folder", "Carpeta", "Dossier", "Ordner"),
"col.size": ("Dimensione", "Size", "Tamaño", "Taille", "Größe"),
"col.class": ("Classe", "Class", "Clase", "Classe", "Klasse"),
"col.state": ("Stato", "State", "Estado", "État", "Status"),
"col.last_use": ("Ultimo uso", "Last use", "Último uso", "Dernière utilisation", "Zuletzt benutzt"),
"col.return": ("Ritorno", "Return", "Regreso", "Retour", "Rückkehr"),
"col.source": ("Fonte", "Source", "Fuente", "Source", "Quelle"),
"col.when": ("Quando", "When", "Cuándo", "Quand", "Wann"),
"col.action": ("Attività", "Action", "Acción", "Action", "Aktion"),
"col.file": ("File", "File", "Archivo", "Fichier", "Datei"),
"col.detail": ("Dettaglio", "Detail", "Detalle", "Détail", "Detail"),
# --- settings
"set.title": ("Impostazioni", "Settings", "Ajustes", "Paramètres", "Einstellungen"),
"set.subtitle": ("Valori prudenti di serie. Ogni modifica vale da subito.", "Cautious defaults. Changes apply immediately.", "Valores prudentes por defecto. Los cambios se aplican al instante.", "Valeurs prudentes par défaut. Les changements s'appliquent immédiatement.", "Vorsichtige Standardwerte. Änderungen gelten sofort."),
"set.language": ("Lingua", "Language", "Idioma", "Langue", "Sprache"),
"set.lang_auto": ("Automatica (come Windows)", "Automatic (as Windows)", "Automático (como Windows)", "Automatique (comme Windows)", "Automatisch (wie Windows)"),
"set.tide_section": ("Marea", "Tide", "Marea", "Marée", "Flut"),
"set.auto": ("Marea automatica", "Automatic tide", "Marea automática", "Marée automatique", "Automatische Flut"),
"set.auto_help": ("Ogni 10 minuti, se lo spazio libero scende sotto la soglia, evapora i file che costa meno riavere. Spenta di serie.", "Every 10 minutes, if free space drops below the target, it evaporates the files that are cheapest to get back. Off by default.", "Cada 10 minutos, si el espacio libre baja del umbral, evapora los archivos más baratos de recuperar. Desactivada por defecto.", "Toutes les 10 minutes, si l'espace libre passe sous le seuil, elle évapore les fichiers les moins coûteux à récupérer. Désactivée par défaut.", "Alle 10 Minuten verdunstet sie, falls der freie Speicher unter das Ziel fällt, die Dateien, die sich am günstigsten zurückholen lassen. Standardmäßig aus."),
"set.target": ("Spazio libero da mantenere (GB)", "Free space to keep (GB)", "Espacio libre a mantener (GB)", "Espace libre à garder (Go)", "Freizuhaltender Speicher (GB)"),
"set.idle": ("Non evaporare file usati negli ultimi (giorni)", "Never evaporate files used in the last (days)", "No evaporar archivos usados en los últimos (días)", "Ne pas évaporer les fichiers utilisés ces derniers (jours)", "Keine Dateien verdunsten, die genutzt wurden in den letzten (Tagen)"),
"set.minsize": ("Ignora file più piccoli di (MB)", "Ignore files smaller than (MB)", "Ignorar archivos menores de (MB)", "Ignorer les fichiers de moins de (Mo)", "Dateien ignorieren kleiner als (MB)"),
"set.bandwidth": ("Velocità di download per le stime (MB/s)", "Download speed for estimates (MB/s)", "Velocidad de descarga para estimaciones (MB/s)", "Débit de téléchargement pour les estimations (Mo/s)", "Download-Geschwindigkeit für Schätzungen (MB/s)"),
"set.sources_section": ("Fonti", "Sources", "Fuentes", "Sources", "Quellen"),
"set.mirror": ("Cartella mirror (per esempio il NAS)", "Mirror folder (e.g. your NAS)", "Carpeta espejo (p. ej. el NAS)", "Dossier miroir (p. ex. le NAS)", "Spiegelordner (z. B. das NAS)"),
"set.pick": ("Scegli…", "Browse…", "Elegir…", "Parcourir…", "Durchsuchen…"),
"set.mirror_help": ("I file unici replicati qui diventano riottenibili. Le copie sono verificate e mai duplicate.", "Unique files replicated here become re-obtainable. Copies are verified and never duplicated.", "Los archivos únicos replicados aquí se vuelven recuperables. Las copias se verifican y nunca se duplican.", "Les fichiers uniques répliqués ici deviennent récupérables. Les copies sont vérifiées et jamais dupliquées.", "Hierhin replizierte einzigartige Dateien werden wiederbeschaffbar. Kopien werden geprüft und nie doppelt abgelegt."),
"set.mirror_missing": ("La cartella mirror non è raggiungibile.", "The mirror folder is not reachable.", "La carpeta espejo no es accesible.", "Le dossier miroir est inaccessible.", "Der Spiegelordner ist nicht erreichbar."),
"set.repos": ("Repository Hugging Face da confrontare (uno per riga)", "Hugging Face repositories to compare with (one per line)", "Repositorios de Hugging Face con los que comparar (uno por línea)", "Dépôts Hugging Face à comparer (un par ligne)", "Hugging-Face-Repositorys zum Abgleich (eines pro Zeile)"),
"set.repos_help": ("Vengono inviati solo i nomi dei repository pubblici: mai nomi di file o percorsi del tuo PC.", "Only public repository names are sent: never file names or paths from your PC.", "Solo se envían nombres de repositorios públicos: nunca nombres de archivo ni rutas de tu PC.", "Seuls les noms de dépôts publics sont envoyés : jamais de noms de fichiers ni de chemins de votre PC.", "Es werden nur öffentliche Repository-Namen gesendet: nie Dateinamen oder Pfade deines PCs."),
"set.startup": ("Avvia Marea con Windows", "Start Marea with Windows", "Iniciar Marea con Windows", "Lancer Marea avec Windows", "Marea mit Windows starten"),
"set.startup_help": ("Necessario: i file evaporati si aprono solo quando Marea è attiva.", "Required: evaporated files open only while Marea is running.", "Necesario: los archivos evaporados solo se abren cuando Marea está activa.", "Nécessaire : les fichiers évaporés ne s'ouvrent que si Marea est active.", "Erforderlich: verdunstete Dateien öffnen sich nur, solange Marea läuft."),
"set.save": ("Salva", "Save", "Guardar", "Enregistrer", "Speichern"),
"set.saved": ("Impostazioni salvate.", "Settings saved.", "Ajustes guardados.", "Paramètres enregistrés.", "Einstellungen gespeichert."),
"set.invalid": ("Controlla i valori: devono essere numeri positivi.", "Check the values: they must be positive numbers.", "Revisa los valores: deben ser números positivos.", "Vérifiez les valeurs : ce doivent être des nombres positifs.", "Prüfe die Werte: Es müssen positive Zahlen sein."),
"set.danger": ("Uscita di emergenza", "Emergency exit", "Salida de emergencia", "Sortie de secours", "Notausgang"),
"set.danger_help": ("Riporta tutti i file sul disco, li trasforma in file normali e sgancia le cartelle da Windows. Da usare prima di disinstallare o se qualcosa non ti convince.", "Brings every file back, turns them into ordinary files and detaches the folders from Windows. Use it before uninstalling or whenever something looks wrong.", "Devuelve todos los archivos al disco, los convierte en archivos normales y desvincula las carpetas de Windows. Úsalo antes de desinstalar o si algo no te convence.", "Ramène tous les fichiers sur le disque, les transforme en fichiers normaux et détache les dossiers de Windows. À utiliser avant de désinstaller ou si quelque chose vous inquiète.", "Holt alle Dateien zurück, macht sie wieder zu normalen Dateien und löst die Ordner von Windows. Vor dem Deinstallieren verwenden oder wenn dir etwas seltsam vorkommt."),
"set.restore_all": ("Riporta tutto e sgancia", "Bring everything back and detach", "Devolver todo y desvincular", "Tout ramener et détacher", "Alles zurückholen und lösen"),
"set.restore_confirm": ("Riportare TUTTI i file evaporati sul disco? Serve spazio libero sufficiente e, per quelli da Hugging Face, la connessione.", "Bring ALL evaporated files back to disk? You need enough free space and, for Hugging Face ones, a connection.", "¿Devolver TODOS los archivos evaporados al disco? Necesitas espacio libre suficiente y, para los de Hugging Face, conexión.", "Ramener TOUS les fichiers évaporés sur le disque ? Il faut assez d'espace libre et, pour ceux de Hugging Face, une connexion.", "ALLE verdunsteten Dateien zurückholen? Dafür brauchst du genug freien Speicher und für die von Hugging Face eine Verbindung."),
"set.restore_done": ("Fatto: nessun file evaporato, le cartelle sono state sganciate da Windows.", "Done: no evaporated files left, folders detached from Windows.", "Hecho: no quedan archivos evaporados y las carpetas se desvincularon de Windows.", "Terminé : plus aucun fichier évaporé, les dossiers sont détachés de Windows.", "Fertig: keine verdunsteten Dateien mehr, die Ordner sind von Windows gelöst."),
"set.about": ("Marea {0} · dati in {1}", "Marea {0} · data in {1}", "Marea {0} · datos en {1}", "Marea {0} · données dans {1}", "Marea {0} · Daten in {1}"),
# --- plan
"plan.title": ("Libera spazio", "Free up space", "Liberar espacio", "Libérer de l'espace", "Speicher freigeben"),
"plan.subtitle": ("Ognuno di questi file ha una fonte verificata in questo momento. Resta nella sua cartella e torna da solo quando un programma lo apre.", "Each of these files has a source verified right now. It stays in its folder and comes back on its own when a program opens it.", "Cada uno de estos archivos tiene ahora mismo una fuente verificada. Se queda en su carpeta y vuelve solo cuando un programa lo abre.", "Chacun de ces fichiers a une source vérifiée à l'instant. Il reste dans son dossier et revient seul quand un programme l'ouvre.", "Jede dieser Dateien hat gerade eine geprüfte Quelle. Sie bleibt in ihrem Ordner und kommt von selbst zurück, wenn ein Programm sie öffnet."),
"plan.total": ("Totale: {0} in {1} file", "Total: {0} in {1} files", "Total: {0} en {1} archivos", "Total : {0} dans {1} fichiers", "Gesamt: {0} in {1} Dateien"),
"plan.understand": ("Ho capito: i file evaporati si aprono solo mentre Marea è attiva.", "I understand: evaporated files open only while Marea is running.", "Entendido: los archivos evaporados solo se abren mientras Marea está activa.", "J'ai compris : les fichiers évaporés ne s'ouvrent que si Marea est active.", "Verstanden: verdunstete Dateien öffnen sich nur, solange Marea läuft."),
"plan.go": ("Evapora", "Evaporate", "Evaporar", "Évaporer", "Verdunsten"),
"plan.empty": ("Niente da liberare in sicurezza adesso. La pagina File spiega perché ogni file resta.", "Nothing can be freed safely right now. The Files page explains why each file stays.", "Ahora no hay nada que liberar con seguridad. La página Archivos explica por qué se queda cada archivo.", "Rien à libérer en sécurité pour l'instant. La page Fichiers explique pourquoi chaque fichier reste.", "Gerade lässt sich nichts sicher freigeben. Die Seite Dateien erklärt, warum jede Datei bleibt."),
"msg.kept_some": ("{0} file evaporati. {1} sono rimasti sul disco:\n\n{2}", "{0} files evaporated. {1} stayed on disk:\n\n{2}", "{0} archivos evaporados. {1} se quedaron en el disco:\n\n{2}", "{0} fichiers évaporés. {1} sont restés sur le disque :\n\n{2}", "{0} Dateien verdunstet. {1} sind auf der Platte geblieben:\n\n{2}"),
"startup.ask": ("I file evaporati si aprono solo quando Marea è attiva. Avviare Marea automaticamente con Windows? (consigliato)", "Evaporated files open only while Marea is running. Start Marea automatically with Windows? (recommended)", "Los archivos evaporados solo se abren cuando Marea está activa. ¿Iniciar Marea automáticamente con Windows? (recomendado)", "Les fichiers évaporés ne s'ouvrent que si Marea est active. Lancer Marea automatiquement avec Windows ? (recommandé)", "Verdunstete Dateien öffnen sich nur, solange Marea läuft. Marea automatisch mit Windows starten? (empfohlen)"),
# --- tray and exit
"tray.open": ("Apri Marea", "Open Marea", "Abrir Marea", "Ouvrir Marea", "Marea öffnen"),
"tray.exit": ("Esci", "Exit", "Salir", "Quitter", "Beenden"),
"tray.none": ("Marea - nessun file evaporato", "Marea - no evaporated files", "Marea - ningún archivo evaporado", "Marea - aucun fichier évaporé", "Marea - keine verdunsteten Dateien"),
"tray.some": ("Marea - {0} evaporati", "Marea - {0} evaporated", "Marea - {0} evaporados", "Marea - {0} évaporés", "Marea - {0} verdunstet"),
"tray.fail_title": ("Un file non è potuto tornare", "A file could not come back", "Un archivo no pudo volver", "Un fichier n'a pas pu revenir", "Eine Datei konnte nicht zurückkommen"),
"tray.freed_title": ("La marea ha liberato spazio", "The tide freed space", "La marea liberó espacio", "La marée a libéré de l'espace", "Die Flut hat Speicher freigegeben"),
"tray.freed_text": ("{0} evaporati: torneranno da soli quando servono.", "{0} evaporated: they will come back on their own when needed.", "{0} evaporados: volverán solos cuando hagan falta.", "{0} évaporés : ils reviendront seuls quand il le faudra.", "{0} verdunstet: Sie kommen von selbst zurück, wenn sie gebraucht werden."),
"exit.confirm": ("{0} file sono evaporati. Finché Marea è chiusa non si potranno aprire (i programmi riceveranno un errore). Torneranno normali al prossimo avvio di Marea.\n\nUscire comunque?", "{0} files are evaporated. While Marea is closed they cannot be opened (programs will get an error). They work again as soon as Marea starts.\n\nExit anyway?", "{0} archivos están evaporados. Mientras Marea esté cerrada no se podrán abrir (los programas recibirán un error). Volverán a funcionar al iniciar Marea.\n\n¿Salir de todos modos?", "{0} fichiers sont évaporés. Tant que Marea est fermée, ils ne pourront pas être ouverts (les programmes recevront une erreur). Ils refonctionnent dès que Marea redémarre.\n\nQuitter quand même ?", "{0} Dateien sind verdunstet. Solange Marea geschlossen ist, lassen sie sich nicht öffnen (Programme erhalten einen Fehler). Sobald Marea startet, funktionieren sie wieder.\n\nTrotzdem beenden?"),
# --- first-run guide
"ob.title": ("Benvenuto in Marea", "Welcome to Marea", "Bienvenido a Marea", "Bienvenue dans Marea", "Willkommen bei Marea"),
"ob.intro": ("Marea libera spazio senza farti perdere nulla. Prima di iniziare, tre cose da sapere.", "Marea frees space without you losing anything. Three things to know before starting.", "Marea libera espacio sin que pierdas nada. Tres cosas que saber antes de empezar.", "Marea libère de l'espace sans rien vous faire perdre. Trois choses à savoir avant de commencer.", "Marea gibt Speicher frei, ohne dass du etwas verlierst. Drei Dinge vorab."),
"ob.point1_title": ("Sa da dove si riavrebbe ogni file", "It knows where every file would come back from", "Sabe de dónde volvería cada archivo", "Elle sait d'où reviendrait chaque fichier", "Es weiß, woher jede Datei zurückkäme"),
"ob.point1": ("Calcola l'impronta di ogni modello e la confronta con Hugging Face, con il tuo NAS e con gli altri file sul disco. Se non trova una fonte certa, il file resta dov'è.", "It fingerprints every model and compares it with Hugging Face, your NAS and the other files on disk. Without a certain source, the file stays where it is.", "Calcula la huella de cada modelo y la compara con Hugging Face, tu NAS y los demás archivos del disco. Sin una fuente segura, el archivo se queda donde está.", "Elle calcule l'empreinte de chaque modèle et la compare avec Hugging Face, votre NAS et les autres fichiers du disque. Sans source certaine, le fichier reste où il est.", "Es berechnet den Fingerabdruck jedes Modells und vergleicht ihn mit Hugging Face, deinem NAS und den anderen Dateien auf der Platte. Ohne sichere Quelle bleibt die Datei, wo sie ist."),
"ob.point2_title": ("Libera spazio senza cancellare", "It frees space without deleting", "Libera espacio sin borrar", "Elle libère de l'espace sans effacer", "Es gibt Speicher frei, ohne zu löschen"),
"ob.point2": ("Un file “evaporato” resta nella sua cartella con il suo nome, ma occupa 0 byte. Quando un programma lo apre, Marea lo riporta e ne verifica l'impronta.", "An “evaporated” file stays in its folder with its name but takes 0 bytes. When a program opens it, Marea brings it back and checks its fingerprint.", "Un archivo “evaporado” se queda en su carpeta con su nombre, pero ocupa 0 bytes. Cuando un programa lo abre, Marea lo devuelve y verifica su huella.", "Un fichier « évaporé » reste dans son dossier avec son nom, mais occupe 0 octet. Quand un programme l'ouvre, Marea le ramène et vérifie son empreinte.", "Eine „verdunstete“ Datei bleibt mit ihrem Namen im Ordner, belegt aber 0 Byte. Öffnet ein Programm sie, holt Marea sie zurück und prüft ihren Fingerabdruck."),
"ob.point3_title": ("Non fa nulla senza il tuo ok", "It does nothing without your OK", "No hace nada sin tu permiso", "Elle ne fait rien sans votre accord", "Es tut nichts ohne dein OK"),
"ob.point3": ("La prima scansione misura e basta. Decidi tu cosa liberare, oppure accendi la marea automatica. Puoi sempre riportare tutto con un clic.", "The first scan only measures. You decide what to free, or turn on the automatic tide. You can always bring everything back with one click.", "El primer escaneo solo mide. Tú decides qué liberar, o activas la marea automática. Siempre puedes devolverlo todo con un clic.", "La première analyse ne fait que mesurer. Vous décidez quoi libérer, ou activez la marée automatique. Vous pouvez toujours tout ramener en un clic.", "Der erste Scan misst nur. Du entscheidest, was frei wird, oder schaltest die automatische Flut ein. Mit einem Klick holst du jederzeit alles zurück."),
"ob.next": ("Avanti", "Next", "Siguiente", "Suivant", "Weiter"),
"ob.back": ("Indietro", "Back", "Atrás", "Retour", "Zurück"),
"ob.finish": ("Inizia", "Start", "Empezar", "Commencer", "Loslegen"),
"ob.add_selected": ("Aggiungi le selezionate", "Add selected", "Añadir seleccionadas", "Ajouter la sélection", "Auswahl hinzufügen"),
"ob.folders_title": ("Le tue cartelle di modelli", "Your model folders", "Tus carpetas de modelos", "Vos dossiers de modèles", "Deine Modellordner"),
"ob.searching": ("Cerco le cartelle di modelli sui tuoi dischi…", "Looking for model folders on your drives…", "Buscando carpetas de modelos en tus discos…", "Recherche des dossiers de modèles sur vos disques…", "Suche Modellordner auf deinen Laufwerken…"),
"ob.found": ("Ho trovato {0} cartelle con modelli. Togli la spunta a quelle che non vuoi far gestire.", "Found {0} folders with models. Untick the ones you don't want managed.", "He encontrado {0} carpetas con modelos. Desmarca las que no quieras que se gestionen.", "{0} dossiers avec des modèles trouvés. Décochez ceux que vous ne voulez pas faire gérer.", "{0} Ordner mit Modellen gefunden. Entferne das Häkchen bei denen, die nicht verwaltet werden sollen."),
"ob.none_found": ("Non ho trovato cartelle di modelli. Puoi aggiungerne una a mano.", "No model folders found. You can add one by hand.", "No he encontrado carpetas de modelos. Puedes añadir una a mano.", "Aucun dossier de modèles trouvé. Vous pouvez en ajouter un manuellement.", "Keine Modellordner gefunden. Du kannst einen von Hand hinzufügen."),
"ob.found_summary": ("{0} file di pesi · {1}", "{0} weight files · {1}", "{0} archivos de pesos · {1}", "{0} fichiers de poids · {1}", "{0} Gewichtsdateien · {1}"),
"ob.add_other": ("Aggiungi un'altra cartella…", "Add another folder…", "Añadir otra carpeta…", "Ajouter un autre dossier…", "Weiteren Ordner hinzufügen…"),
"ob.added_by_you": ("aggiunta da te", "added by you", "añadida por ti", "ajouté par vous", "von dir hinzugefügt"),
# --- engine: safety rules
"gate.not_local": ("non è sul disco", "it is not on disk", "no está en el disco", "il n'est pas sur le disque", "sie ist nicht auf der Platte"),
"gate.outside_roots": ("è fuori dalle cartelle gestite", "it is outside the managed folders", "está fuera de las carpetas gestionadas", "il est hors des dossiers gérés", "sie liegt außerhalb der verwalteten Ordner"),
"gate.recently_used": ("usato {0} giorni fa (minimo {1})", "used {0} days ago (minimum {1})", "usado hace {0} días (mínimo {1})", "utilisé il y a {0} jours (minimum {1})", "vor {0} Tagen benutzt (Minimum {1})"),
"gate.pinned": ("serve a ricostruire un file già evaporato", "it is needed to rebuild a file that is already evaporated", "hace falta para reconstruir un archivo ya evaporado", "il sert à reconstruire un fichier déjà évaporé", "sie wird gebraucht, um eine bereits verdunstete Datei wiederherzustellen"),
"gate.unique": ("unico: nessuna fonte verificata (replicalo sul mirror per poterlo evaporare)", "unique: no verified source (replicate it to the mirror to be able to evaporate it)", "único: sin fuente verificada (replícalo en el espejo para poder evaporarlo)", "unique : aucune source vérifiée (répliquez-le sur le miroir pour pouvoir l'évaporer)", "einzigartig: keine geprüfte Quelle (repliziere sie auf den Spiegel, um sie verdunsten zu können)"),
"gate.source_leaving": ("la sua unica fonte evaporerebbe insieme a lui", "its only source would evaporate together with it", "su única fuente se evaporaría junto con él", "sa seule source s'évaporerait avec lui", "ihre einzige Quelle würde mit ihr verdunsten"),
"ev.no_source_alive": ("nessuna fonte risponde in questo momento", "no source answers right now", "ninguna fuente responde en este momento", "aucune source ne répond en ce moment", "gerade antwortet keine Quelle"),
"ev.unexpected_state": ("stato del file inatteso: {0}", "unexpected file state: {0}", "estado de archivo inesperado: {0}", "état de fichier inattendu : {0}", "unerwarteter Dateistatus: {0}"),
"ev.in_use": ("il file è in uso da un altro programma", "the file is in use by another program", "el archivo lo está usando otro programa", "le fichier est utilisé par un autre programme", "die Datei wird von einem anderen Programm verwendet"),
"ev.hardlinks": ("il file ha altri collegamenti (hardlink)", "the file has other hard links", "el archivo tiene otros enlaces duros", "le fichier a d'autres liens physiques", "die Datei hat weitere Hardlinks"),
"ev.changed": ("il file è cambiato dall'ultima impronta: va scansionato di nuovo", "the file changed since its last fingerprint: scan again", "el archivo cambió desde su última huella: vuelve a escanear", "le fichier a changé depuis sa dernière empreinte : relancez l'analyse", "die Datei hat sich seit dem letzten Fingerabdruck geändert: erneut scannen"),
"ev.public_changed": ("il file pubblico non corrisponde più", "the public file no longer matches", "el archivo público ya no coincide", "le fichier public ne correspond plus", "die öffentliche Datei stimmt nicht mehr überein"),
"root.missing": ("La cartella non esiste.", "The folder does not exist.", "La carpeta no existe.", "Le dossier n'existe pas.", "Der Ordner existiert nicht."),
"root.not_fixed": ("Solo dischi locali: Marea non gestisce unità di rete o rimovibili.", "Local disks only: Marea does not manage network or removable drives.", "Solo discos locales: Marea no gestiona unidades de red ni extraíbles.", "Disques locaux uniquement : Marea ne gère pas les lecteurs réseau ou amovibles.", "Nur lokale Platten: Marea verwaltet keine Netzwerk- oder Wechsellaufwerke."),
"root.not_ntfs": ("Il disco deve essere NTFS.", "The drive must be NTFS.", "El disco debe ser NTFS.", "Le disque doit être en NTFS.", "Das Laufwerk muss NTFS sein."),
"root.whole_drive": ("Non si può gestire un intero disco: scegli una cartella.", "A whole drive cannot be managed: choose a folder.", "No se puede gestionar un disco entero: elige una carpeta.", "Un disque entier ne peut pas être géré : choisissez un dossier.", "Ein ganzes Laufwerk kann nicht verwaltet werden: wähle einen Ordner."),
"root.system": ("Cartella di sistema o troppo ampia: scegli una sottocartella precisa (per esempio checkpoints).", "System or too broad a folder: choose a specific subfolder (e.g. checkpoints).", "Carpeta del sistema o demasiado amplia: elige una subcarpeta concreta (p. ej. checkpoints).", "Dossier système ou trop large : choisissez un sous-dossier précis (p. ex. checkpoints).", "Systemordner oder zu allgemein: wähle einen bestimmten Unterordner (z. B. checkpoints)."),
"root.foreign": ("La cartella è già gestita da un altro servizio (per esempio OneDrive).", "The folder is already managed by another service (e.g. OneDrive).", "La carpeta ya la gestiona otro servicio (p. ej. OneDrive).", "Le dossier est déjà géré par un autre service (p. ex. OneDrive).", "Der Ordner wird bereits von einem anderen Dienst verwaltet (z. B. OneDrive)."),
"root.already": ("La cartella è già gestita.", "The folder is already managed.", "La carpeta ya está gestionada.", "Le dossier est déjà géré.", "Der Ordner wird bereits verwaltet."),
"root.overlap": ("Si sovrappone a una cartella già gestita: {0}", "It overlaps a folder already managed: {0}", "Se solapa con una carpeta ya gestionada: {0}", "Il chevauche un dossier déjà géré : {0}", "Er überschneidet sich mit einem bereits verwalteten Ordner: {0}"),
"hub.bad_repo": ("Nome di repository non valido: {0}", "Invalid repository name: {0}", "Nombre de repositorio no válido: {0}", "Nom de dépôt invalide : {0}", "Ungültiger Repository-Name: {0}"),
"hub.status": ("Hugging Face ha risposto {0} per {1}", "Hugging Face answered {0} for {1}", "Hugging Face respondió {0} para {1}", "Hugging Face a répondu {0} pour {1}", "Hugging Face antwortete {0} für {1}"),
"hub.host_denied": ("Destinazione non consentita: {0}", "Destination not allowed: {0}", "Destino no permitido: {0}", "Destination non autorisée : {0}", "Ziel nicht erlaubt: {0}"),
"hub.redirects": ("Troppi reindirizzamenti da Hugging Face.", "Too many redirects from Hugging Face.", "Demasiadas redirecciones de Hugging Face.", "Trop de redirections depuis Hugging Face.", "Zu viele Weiterleitungen von Hugging Face."),
"hyd.size_differs": ("{0}: dimensione diversa", "{0}: different size", "{0}: tamaño distinto", "{0} : taille différente", "{0}: andere Größe"),
"hyd.no_source": ("nessuna fonte disponibile", "no source available", "ninguna fuente disponible", "aucune source disponible", "keine Quelle verfügbar"),
"hyd.source_stopped": ("la fonte si è interrotta", "the source stopped", "la fuente se interrumpió", "la source s'est interrompue", "die Quelle brach ab"),
"hyd.mismatch_detail": ("da {0}: il file viene evaporato di nuovo per riscaricarlo da capo", "from {0}: the file is evaporated again to fetch it from scratch", "desde {0}: el archivo se evapora de nuevo para descargarlo desde cero", "depuis {0} : le fichier est de nouveau évaporé pour être récupéré à neuf", "von {0}: die Datei wird erneut verdunstet, um sie neu zu holen"),
"hyd.close_and_retry": ("file in uso: chiudi il programma e riprova", "file in use: close the program and try again", "archivo en uso: cierra el programa y vuelve a intentarlo", "fichier utilisé : fermez le programme et réessayez", "Datei in Verwendung: Programm schließen und erneut versuchen"),
"eng.came_back_different": ("{0}: il contenuto tornato non corrisponde all'atteso. Controlla le Attività.", "{0}: the content that came back does not match. Check Activity.", "{0}: el contenido devuelto no coincide. Revisa la Actividad.", "{0} : le contenu revenu ne correspond pas. Consultez l'Activité.", "{0}: der zurückgekommene Inhalt stimmt nicht. Prüfe die Aktivität."),
"eng.no_mirror": ("Imposta prima una cartella mirror raggiungibile (per esempio il NAS).", "First set a reachable mirror folder (e.g. your NAS).", "Primero configura una carpeta espejo accesible (p. ej. el NAS).", "Définissez d'abord un dossier miroir accessible (p. ex. le NAS).", "Lege zuerst einen erreichbaren Spiegelordner fest (z. B. das NAS)."),
"eng.placeholders_left": ("Non sgancio {0}: ci sono ancora file evaporati e Windows li cancellerebbe.\n{1}", "Not detaching {0}: evaporated files are still there and Windows would delete them.\n{1}", "No desvinculo {0}: aún hay archivos evaporados y Windows los borraría.\n{1}", "Je ne détache pas {0} : il reste des fichiers évaporés que Windows supprimerait.\n{1}", "{0} wird nicht gelöst: es gibt noch verdunstete Dateien, die Windows löschen würde.\n{1}"),
"eng.no_space": ("Spazio insufficiente su {0}: servono {1} GB, liberi {2} GB. Libera spazio prima di riportare i file.", "Not enough space on {0}: {1} GB needed, {2} GB free. Free some space before bringing files back.", "Espacio insuficiente en {0}: se necesitan {1} GB, hay {2} GB libres. Libera espacio antes de devolver los archivos.", "Espace insuffisant sur {0} : {1} Go nécessaires, {2} Go libres. Libérez de l'espace avant de ramener les fichiers.", "Zu wenig Speicher auf {0}: {1} GB nötig, {2} GB frei. Gib Speicher frei, bevor du Dateien zurückholst."),
"eng.tide_detail": ("{0} file evaporati per tornare sopra la soglia", "{0} files evaporated to get back above target", "{0} archivos evaporados para volver por encima del umbral", "{0} fichiers évaporés pour repasser au-dessus du seuil", "{0} Dateien verdunstet, um wieder über das Ziel zu kommen"),
"mirror.mismatch": ("La copia sul mirror non corrisponde all'originale.", "The mirror copy does not match the original.", "La copia del espejo no coincide con el original.", "La copie du miroir ne correspond pas à l'original.", "Die Spiegelkopie stimmt nicht mit dem Original überein."),
"recipe.short_source": ("Il file sorgente della ricetta è più corto del previsto.", "The recipe's source file is shorter than expected.", "El archivo de origen de la receta es más corto de lo previsto.", "Le fichier source de la recette est plus court que prévu.", "Die Quelldatei des Rezepts ist kürzer als erwartet."),
"recipe.no_segment": ("La ricetta non copre l'intervallo richiesto.", "The recipe does not cover the requested range.", "La receta no cubre el rango solicitado.", "La recette ne couvre pas la plage demandée.", "Das Rezept deckt den angeforderten Bereich nicht ab."),
# --- sources
"src.copy": ("copia identica: {0}", "identical copy: {0}", "copia idéntica: {0}", "copie identique : {0}", "identische Kopie: {0}"),
"src.hf": ("Hugging Face: {0}", "Hugging Face: {0}", "Hugging Face: {0}", "Hugging Face : {0}", "Hugging Face: {0}"),
"src.hf_short": ("Hugging Face {0}", "Hugging Face {0}", "Hugging Face {0}", "Hugging Face {0}", "Hugging Face {0}"),
"src.local_copy": ("copia locale", "local copy", "copia local", "copie locale", "lokale Kopie"),
"src.mirror": ("mirror: {0}", "mirror: {0}", "espejo: {0}", "miroir : {0}", "Spiegel: {0}"),
"src.mirror_short": ("mirror", "mirror", "espejo", "miroir", "Spiegel"),
"src.none": ("nessuna fonte: resta sul disco", "no source: stays on disk", "sin fuente: se queda en el disco", "aucune source : reste sur le disque", "keine Quelle: bleibt auf der Platte"),
"src.recipe_cast": ("ricalcolo bf16 da fp32 ({0})", "bf16 recomputed from fp32 ({0})", "bf16 recalculado desde fp32 ({0})", "bf16 recalculé depuis fp32 ({0})", "bf16 aus fp32 neu berechnet ({0})"),
"src.recipe_compose": ("ricomposizione da tensori di {0}", "recomposed from tensors of {0}", "recompuesto con tensores de {0}", "recomposé à partir des tenseurs de {0}", "aus Tensoren von {0} zusammengesetzt"),
# --- phases
"phase.hash": ("Calcolo impronta", "Fingerprinting", "Calculando huella", "Calcul de l'empreinte", "Fingerabdruck berechnen"),
"phase.hub": ("Confronto con Hugging Face", "Comparing with Hugging Face", "Comparando con Hugging Face", "Comparaison avec Hugging Face", "Abgleich mit Hugging Face"),
"phase.recipe": ("Verifica ricetta (ricostruzione completa)", "Verifying recipe (full rebuild)", "Verificando receta (reconstrucción completa)", "Vérification de la recette (reconstruction complète)", "Rezept prüfen (vollständiger Neuaufbau)"),
"phase.verify_bits": ("Verifica bit a bit", "Bit-by-bit check", "Verificación bit a bit", "Vérification bit à bit", "Bit-für-Bit-Prüfung"),
"phase.bring_back": ("Riporto sul disco", "Bringing back", "Devolviendo", "Retour", "Zurückholen"),
"phase.replicate": ("Replica sul mirror", "Replicating to mirror", "Replicando en el espejo", "Réplication sur le miroir", "Auf Spiegel replizieren"),
# --- journal (actions)
"j.evaporated": ("evaporato", "evaporated", "evaporado", "évaporé", "verdunstet"),
"j.kept": ("lasciato sul disco", "kept on disk", "dejado en el disco", "laissé sur le disque", "auf der Platte gelassen"),
"j.returned_verified": ("tornato e verificato", "came back, verified", "volvió y verificado", "revenu et vérifié", "zurück und geprüft"),
"j.return_failed": ("ritorno fallito", "return failed", "regreso fallido", "retour échoué", "Rückkehr fehlgeschlagen"),
"j.mismatch": ("CONTENUTO NON CORRISPONDENTE", "CONTENT MISMATCH", "CONTENIDO NO COINCIDENTE", "CONTENU NON CONFORME", "INHALT STIMMT NICHT"),
"j.action_needed": ("intervento richiesto", "action needed", "se requiere intervención", "intervention requise", "Eingriff nötig"),
"j.verify_postponed": ("verifica rimandata", "check postponed", "verificación aplazada", "vérification reportée", "Prüfung verschoben"),
"j.source_gone": ("fonte scomparsa", "source gone", "fuente desaparecida", "source disparue", "Quelle verschwunden"),
"j.source_unreachable": ("fonte non raggiungibile", "source unreachable", "fuente inaccesible", "source injoignable", "Quelle nicht erreichbar"),
"j.recipe_verified": ("ricetta verificata", "recipe verified", "receta verificada", "recette vérifiée", "Rezept geprüft"),
"j.removed": ("uscito dal registro", "left the ledger", "salió del registro", "sorti du registre", "aus dem Register entfernt"),
"j.user_modified": ("modificato dall'utente", "modified by the user", "modificado por el usuario", "modifié par l'utilisateur", "vom Benutzer geändert"),
"j.root_added": ("cartella aggiunta", "folder added", "carpeta añadida", "dossier ajouté", "Ordner hinzugefügt"),
"j.root_removed": ("cartella rilasciata", "folder released", "carpeta liberada", "dossier libéré", "Ordner freigegeben"),
"j.brought_back": ("riportato sul disco", "brought back to disk", "devuelto al disco", "ramené sur le disque", "zurückgeholt"),
"j.bring_back_failed": ("ritorno non riuscito", "bring-back failed", "no se pudo devolver", "retour impossible", "Zurückholen fehlgeschlagen"),
"j.restore_all": ("ripristino completo", "full restore", "restauración completa", "restauration complète", "vollständige Wiederherstellung"),
"j.replicated": ("replicato sul mirror", "replicated to mirror", "replicado en el espejo", "répliqué sur le miroir", "auf Spiegel repliziert"),
"j.replicate_failed": ("replica non riuscita", "replication failed", "réplica fallida", "réplication échouée", "Replikation fehlgeschlagen"),
"j.tide": ("marea", "tide", "marea", "marée", "Flut"),
"j.tide_interrupted": ("marea interrotta", "tide interrupted", "marea interrumpida", "marée interrompue", "Flut unterbrochen"),
"j.op_cancelled": ("operazione annullata", "operation cancelled", "operación cancelada", "opération annulée", "Vorgang abgebrochen"),
"j.op_failed": ("operazione non riuscita", "operation failed", "operación fallida", "opération échouée", "Vorgang fehlgeschlagen"),
# --- command line
"cli.added": ("Cartella aggiunta. Ora esegui: mareactl scan", "Folder added. Now run: mareactl scan", "Carpeta añadida. Ahora ejecuta: mareactl scan", "Dossier ajouté. Lancez maintenant : mareactl scan", "Ordner hinzugefügt. Jetzt ausführen: mareactl scan"),
"cli.removed": ("Cartella rilasciata: tutti i file sono tornati normali.", "Folder released: all files are ordinary again.", "Carpeta liberada: todos los archivos vuelven a ser normales.", "Dossier libéré : tous les fichiers sont redevenus normaux.", "Ordner freigegeben: alle Dateien sind wieder normal."),
"cli.scanned": ("Scansione completata ({0} file con impronta nuova).", "Scan complete ({0} files newly fingerprinted).", "Escaneo completado ({0} archivos con huella nueva).", "Analyse terminée ({0} fichiers avec nouvelle empreinte).", "Scan abgeschlossen ({0} Dateien neu erfasst)."),
"cli.need_yes": ("\nNessuna modifica. Aggiungi --yes per evaporare questi file.", "\nNothing changed. Add --yes to evaporate these files.", "\nSin cambios. Añade --yes para evaporar estos archivos.", "\nAucun changement. Ajoutez --yes pour évaporer ces fichiers.", "\nNichts geändert. Mit --yes werden diese Dateien verdunstet."),
"cli.evaporated": ("Evaporati {0} file.", "{0} files evaporated.", "{0} archivos evaporados.", "{0} fichiers évaporés.", "{0} Dateien verdunstet."),
"cli.kept": ("lasciato", "kept", "dejado", "laissé", "behalten"),
"cli.not_tracked": ("File non presente nel registro.", "File not in the ledger.", "Archivo no presente en el registro.", "Fichier absent du registre.", "Datei nicht im Register."),
"cli.restored": ("File riportato sul disco.", "File brought back to disk.", "Archivo devuelto al disco.", "Fichier ramené sur le disque.", "Datei zurückgeholt."),
"cli.restore_all_need_yes": ("Riporta TUTTI i file sul disco e sgancia le cartelle da Windows. Aggiungi --yes per procedere.", "Brings ALL files back and detaches the folders from Windows. Add --yes to proceed.", "Devuelve TODOS los archivos al disco y desvincula las carpetas de Windows. Añade --yes para continuar.", "Ramène TOUS les fichiers et détache les dossiers de Windows. Ajoutez --yes pour continuer.", "Holt ALLE Dateien zurück und löst die Ordner von Windows. Mit --yes fortfahren."),
"cli.close_app_first": ("Chiudi prima l'app Marea (o il comando agent): il ripristino deve servire i file da qui.", "Close the Marea app (or the agent command) first: the restore must serve the files from here.", "Cierra primero la app Marea (o el comando agent): la restauración debe servir los archivos desde aquí.", "Fermez d'abord l'application Marea (ou la commande agent) : la restauration doit servir les fichiers d'ici.", "Schließe zuerst die Marea-App (oder den agent-Befehl): die Wiederherstellung muss die Dateien von hier liefern."),
"cli.restored_all": ("Ripristino completo: nessun file evaporato, cartelle sganciate da Windows.", "Full restore: no evaporated files, folders detached from Windows.", "Restauración completa: ningún archivo evaporado, carpetas desvinculadas de Windows.", "Restauration complète : aucun fichier évaporé, dossiers détachés de Windows.", "Vollständig wiederhergestellt: keine verdunsteten Dateien, Ordner von Windows gelöst."),
"cli.replicated": ("Replicati sul mirror: {0} file.", "Replicated to mirror: {0} files.", "Replicados en el espejo: {0} archivos.", "Répliqués sur le miroir : {0} fichiers.", "Auf Spiegel repliziert: {0} Dateien."),
"cli.already_serving": ("Un'altra istanza di Marea sta già servendo i file.", "Another Marea instance is already serving the files.", "Otra instancia de Marea ya está sirviendo los archivos.", "Une autre instance de Marea sert déjà les fichiers.", "Eine andere Marea-Instanz liefert die Dateien bereits."),
"cli.agent_listening": ("Marea è in ascolto: i file evaporati tornano quando vengono letti. Ctrl+C per uscire.", "Marea is listening: evaporated files come back when read. Ctrl+C to exit.", "Marea está a la escucha: los archivos evaporados vuelven al leerse. Ctrl+C para salir.", "Marea est à l'écoute : les fichiers évaporés reviennent quand ils sont lus. Ctrl+C pour quitter.", "Marea lauscht: verdunstete Dateien kommen beim Lesen zurück. Strg+C zum Beenden."),
"cli.returned": ("tornato", "back", "vuelto", "revenu", "zurück"),
"cli.error": ("ERRORE", "ERROR", "ERROR", "ERREUR", "FEHLER"),
"cli.tide_freed": ("Marea: liberati {0}", "Tide: freed {0}", "Marea: liberados {0}", "Marée : {0} libérés", "Flut: {0} freigegeben"),
"cli.missing_arg": ("Manca un argomento.", "Missing argument.", "Falta un argumento.", "Argument manquant.", "Argument fehlt."),
"cli.volume": ("{0}  liberi {1} su {2}  (soglia {3})", "{0}  {1} free of {2}  (target {3})", "{0}  {1} libres de {2}  (umbral {3})", "{0}  {1} libres sur {2}  (seuil {3})", "{0}  {1} frei von {2}  (Ziel {3})"),
"cli.ledger": ("{0} file nel registro, {1}", "{0} files in the ledger, {1}", "{0} archivos en el registro, {1}", "{0} fichiers dans le registre, {1}", "{0} Dateien im Register, {1}"),
"cli.freeable": ("Puoi liberare in sicurezza adesso: {0}", "You can safely free now: {0}", "Puedes liberar con seguridad ahora: {0}", "Vous pouvez libérer en sécurité maintenant : {0}", "Jetzt sicher freigebbar: {0}"),
}

HELP = {
"it": """marea status                     stato dei dischi e del registro
mareactl add <cartella>             gestisci una cartella (es. ...\\checkpoints)
mareactl remove <cartella>          riporta tutto e smetti di gestirla
mareactl scan                       impronte, fonti (Hugging Face, mirror, ricette), classi
mareactl free [--gb N|--all] [--instant] --yes   libera spazio in fretta (annullabile)
mareactl undo                    annulla l'ultima liberazione
mareactl plan [--gb N]              cosa evaporerebbe la marea (senza toccare nulla)
mareactl evaporate [--gb N] --yes   evapora secondo il piano
mareactl restore <file>             riporta subito un file sul disco
mareactl replicate                  copia i file unici sul mirror (NAS)
mareactl restore-all --yes          uscita di emergenza: riporta tutto, sgancia tutto
mareactl journal [n]                ultime attività
mareactl agent                      servi i file evaporati senza l'app aperta""",
"en": """marea status                     disk and ledger status
mareactl add <folder>               manage a folder (e.g. ...\\checkpoints)
mareactl remove <folder>            bring everything back and stop managing it
mareactl scan                       fingerprints, sources (Hugging Face, mirror, recipes), classes
mareactl free [--gb N|--all] [--instant] --yes   free space fast (undoable)
mareactl undo                    undo the last cleanup
mareactl plan [--gb N]              what the tide would evaporate (touches nothing)
mareactl evaporate [--gb N] --yes   evaporate according to the plan
mareactl restore <file>             bring a file back to disk now
mareactl replicate                  copy unique files to the mirror (NAS)
mareactl restore-all --yes          emergency exit: bring everything back, detach everything
mareactl journal [n]                recent activity
mareactl agent                      serve evaporated files without the app open""",
"es": """marea status                     estado de los discos y del registro
mareactl add <carpeta>              gestionar una carpeta (p. ej. ...\\checkpoints)
mareactl remove <carpeta>           devolverlo todo y dejar de gestionarla
mareactl scan                       huellas, fuentes (Hugging Face, espejo, recetas), clases
mareactl free [--gb N|--all] [--instant] --yes   liberar espacio rápido (se puede deshacer)
mareactl undo                    deshacer la última liberación
mareactl plan [--gb N]              qué evaporaría la marea (sin tocar nada)
mareactl evaporate [--gb N] --yes   evaporar según el plan
mareactl restore <archivo>          devolver un archivo al disco ahora
mareactl replicate                  copiar los archivos únicos al espejo (NAS)
mareactl restore-all --yes          salida de emergencia: devolverlo todo, desvincularlo todo
mareactl journal [n]                actividad reciente
mareactl agent                      servir los archivos evaporados sin la app abierta""",
"fr": """marea status                     état des disques et du registre
mareactl add <dossier>              gérer un dossier (p. ex. ...\\checkpoints)
mareactl remove <dossier>           tout ramener et ne plus le gérer
mareactl scan                       empreintes, sources (Hugging Face, miroir, recettes), classes
mareactl free [--gb N|--all] [--instant] --yes   libérer de l'espace vite (annulable)
mareactl undo                    annuler la dernière libération
mareactl plan [--gb N]              ce que la marée évaporerait (sans rien toucher)
mareactl evaporate [--gb N] --yes   évaporer selon le plan
mareactl restore <fichier>          ramener un fichier sur le disque maintenant
mareactl replicate                  copier les fichiers uniques sur le miroir (NAS)
mareactl restore-all --yes          sortie de secours : tout ramener, tout détacher
mareactl journal [n]                activité récente
mareactl agent                      servir les fichiers évaporés sans l'application ouverte""",
"de": """marea status                     Status von Laufwerken und Register
mareactl add <Ordner>               einen Ordner verwalten (z. B. ...\\checkpoints)
mareactl remove <Ordner>            alles zurückholen und nicht mehr verwalten
mareactl scan                       Fingerabdrücke, Quellen (Hugging Face, Spiegel, Rezepte), Klassen
mareactl free [--gb N|--all] [--instant] --yes   schnell Speicher freigeben (rückgängig machbar)
mareactl undo                    letzte Freigabe rückgängig machen
mareactl plan [--gb N]              was die Flut verdunsten würde (ändert nichts)
mareactl evaporate [--gb N] --yes   nach Plan verdunsten
mareactl restore <Datei>            eine Datei sofort zurückholen
mareactl replicate                  einzigartige Dateien auf den Spiegel (NAS) kopieren
mareactl restore-all --yes          Notausgang: alles zurückholen, alles lösen
mareactl journal [n]                letzte Aktivität
mareactl agent                      verdunstete Dateien ohne geöffnete App liefern""",
}

if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "..", "src", "Marea.Core", "i18n")
    os.makedirs(out, exist_ok=True)
    for i, lang in enumerate(LANGS):
        d = {k: v[i] for k, v in T.items()}
        d["cli.help"] = HELP[lang]
        with open(os.path.join(out, f"{lang}.json"), "w", encoding="utf-8") as f:
            json.dump(dict(sorted(d.items())), f, ensure_ascii=False, indent=1)
    print(len(T) + 1, "keys x", len(LANGS), "languages")
