namespace viewer {

    /// <summary>
    /// Il sito del Portale Antenati. Ogni tipo di pagina (Archivio, Fondo, Serie, AnnoSerie, AnnoSerieKind,
    /// Registro) compone da qui i propri indirizzi; i server di dati (immagini, manifest) hanno i loro
    /// (IiifServer, ManifestLoader) e controllano che le richieste vengano da qui (PageLoader.siteHeaders).
    /// </summary>
    public static class AntenatiSite {
        /// <summary>Provenienza, senza barra finale: Referer e Origin delle richieste</summary>
        public const string origin = "https://antenati.cultura.gov.it";

        /// <summary>Home, base degli indirizzi delle pagine</summary>
        public const string root = origin + "/";
    }
}
