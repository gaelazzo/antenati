# Antenati

Un'applicazione per l'analisi e l'estrazione di dati dai registri degli Antenati presenti sul sito degli [Archivi di Stato italiani](http://www.antenati.san.beniculturali.it/).

## Descrizione

Consultare i registri digitalizzati sul portale "Antenati" del Sistema
Archivistico Nazionale significa attendere il caricamento a ogni cambio di
pagina, ritrovare ogni volta l'inquadratura e perdere il filo della ricerca.
Questa applicazione elimina quell'attrito: scarica in anticipo le pagine
vicine a quella in lettura e ne conserva posizione e zoom, restituendo la
continuità di sfogliare il volume originale.

Attorno alla consultazione si aggiunge quanto serve a condurre una ricerca
genealogica nel tempo: un archivio personale in formato GEDCOM interrogabile
mentre si legge, appunti legati ai registri e alle località, segnalibri e
riferimenti incrociati fra indici e atti.

## Funzionalità

- Consultazione fluida con precaricamento delle pagine e zoom persistente
- Navigazione della gerarchia degli archivi con struttura memorizzata in locale
- Cache locale delle immagini: le pagine già viste si riaprono all'istante
- Elaborazione dell'immagine per migliorare la leggibilità delle scritture
- Appunti su tre livelli: per registro, per città e generali
- Segnalibri, cronologia di navigazione e indirizzi in forma compressa
- Riferimenti incrociati fra indici alfabetici e atti corrispondenti
- Archivio genealogico personale in formato GEDCOM, con ricerca in tempo reale

## Prerequisiti

- .NET Framework
- Sistema operativo Windows
- Connessione internet per accedere ai registri online

## Installazione

1. Clona il repository:
```
git clone https://github.com/gaelazzo/antenati.git
```

2. Apri la soluzione in Visual Studio

3. Compila il progetto

4. Esegui l'applicazione

## Utilizzo

> Bozza da rivedere: alcuni dettagli d'uso vanno confermati.

L'obiettivo principale dell'applicazione è rendere **fluida la consultazione**
dei registri: le pagine vicine a quella corrente vengono scaricate in anticipo,
e passando da una all'altra si conservano posizione e livello di zoom. Si
sfoglia così un registro digitalizzato con la stessa continuità con cui si
sfoglierebbe il volume originale, senza attese a ogni cambio di pagina.

Attorno a questo si aggiunge la gestione di un **archivio genealogico
personale** in stile GEDCOM, in cui annotare i propri antenati e cercarli in
tempo reale mentre si consultano i registri.

### Struttura dell'archivio

L'applicazione rispecchia la gerarchia del portale Antenati:

```
Archivio → Fondo → Serie → Anno → Registro → Immagini
```

Ogni livello è navigabile dall'albero nel tab **explorer**; il doppio click su
un nodo lo espande scaricandone il contenuto dal portale. La struttura
scaricata viene memorizzata in `Archivi.json`, così alle sessioni successive
l'albero è già disponibile senza rinterrogare il sito. Il pulsante **Rileggi
archivi** forza il riscaricamento.

### Consultazione delle immagini

| Comando | Azione |
| --- | --- |
| Rotella del mouse | Zoom avanti / indietro |
| Trascinamento col tasto sinistro | Sposta l'immagine |
| Shift + trascinamento | Traccia un rettangolo di selezione |
| Click durante la selezione | Blocca / sblocca il rettangolo |
| `Esc` | Annulla la selezione |
| `←` / `→` | Pagina precedente / successiva |

La navigazione da tastiera va abilitata con la casella **abilita tasti**.
Il passo di spostamento si moltiplica con i modificatori: `Shift` ×10,
`Ctrl` ×5, entrambi ×50 — utile per attraversare rapidamente un registro
di centinaia di pagine. Gli stessi modificatori valgono per i pulsanti **+1**
e **−1**.

Mentre si sfoglia, le pagine successive (o precedenti, secondo la direzione di
marcia) vengono scaricate in background: l'indicatore accanto ai comandi mostra
quante ne sono in coda. Le immagini arrivano dal servizio IIIF degli Antenati e
si accumulano nella cartella `data/`, così le pagine già viste — o precaricate —
si riaprono immediatamente anche in sessioni successive.

Accanto al numero di pagina richiesto è indicato quello **effettivamente**
visualizzato; quando i due divergono il campo si colora, segnalando che
l'immagine mostrata non è ancora quella richiesta. La casella **Serial mode**
impedisce di avanzare finché i due numeri non coincidono: sfogliando in
sequenza si evita così di superare le pagine che stanno ancora arrivando.

### Elaborazione dell'immagine

Per migliorare la leggibilità delle scritture antiche sono disponibili scala di
grigi, equalizzazione, regolazione del contrasto (aumento, riduzione, valore
specifico), rilevamento dei bordi, rotazione a sinistra e capovolgimento.

### Appunti

Gli appunti si tengono su tre livelli distinti, ciascuno con il proprio ambito:

| Livello | File | Ambito |
| --- | --- | --- |
| Per nodo | `appunti.txt` | Legati al registro o al nodo dell'albero |
| Per città | `appuntiCitta.txt` | Legati alla località |
| Generali | `note.txt`, `note2.txt` | Blocco note libero |

Gli appunti di nodo e di città seguono automaticamente la navigazione: si apre
un registro e compaiono le annotazioni prese in precedenza su quel registro.

### Segnalibri e riferimenti

Le pagine di interesse si contrassegnano con **importante**: l'immagine viene
copiata in `important/` e il riferimento annotato in `important.json`. Per ogni
pagina segnata viene generato anche un **indirizzo compresso** — gli URL degli
Antenati sono lunghi e illeggibili, la forma breve è annotabile e condivisibile.
La corrispondenza tra forma breve ed estesa è conservata in
`important_squized.json`, e il pulsante di decodifica ricostruisce l'indirizzo
completo a partire da quello breve.

La navigazione mantiene inoltre una **cronologia** delle pagine visitate,
percorribile avanti e indietro, e una **pila** di posizioni a cui tornare: il
pulsante di aggiunta cambia colore per indicare se la pagina corrente è già
stata visitata.

### Indici e riferimenti incrociati

Molti registri sono corredati da indici alfabetici. L'applicazione permette di
generare e integrare gli indici e di annotare i **riferimenti incrociati** fra
indice e registro, così che dal nome trovato nell'indice si raggiunga
direttamente l'atto corrispondente.

### Archivio genealogico personale

Dal tab dedicato si legge un file GEDCOM esistente e si esporta il proprio
archivio nello stesso formato, per l'interscambio con i programmi di
genealogia.

Con l'archivio caricato si può **cercare una persona in tempo reale** mentre si
consultano i registri: digitando un nome nel campo di ricerca compaiono
immediatamente tutti gli omonimi già raccolti, ciascuno con anni di nascita e
morte, genitori e matrimoni. Cercando `giuseppe alloggio` si ottiene ad esempio:

```text
Giuseppe Alloggio(1882-1951) DI Antonio(1843) E DI Maria Piazzolla(1847)[1864], marito di Giuseppina Minapoli[1900], marito di Ida Bellezza[1920]
Giuseppe Alloggio(1824), marito di Maria Gobbo
Giuseppe Alloggio(1881) DI Antonio(1843) E DI Maria Piazzolla(1847)[1864]
giuseppe alloggio(1890) DI Michele E DI Filomena Giannella(1840)
```

È il confronto che serve davanti a un atto: distinguere fra omonimi, capire se
la persona incontrata è già nota, riconoscere quale ramo si sta leggendo. Le
caselle disponibili regolano il livello di dettaglio — date complete anziché
soli anni, nomi estesi, coniugi, identificativi.

Il tab **Long** approfondisce un singolo individuo: inserito il suo
identificativo GEDCOM, ne restituisce la scheda estesa su più righe, con
coniugi, genitori e figli. Dove la ricerca per nome serve a confrontare molte
persone in una riga ciascuna, qui si esamina una sola persona per intero.

### Obiettivi di ricerca

La ricerca genealogica procede per obiettivi che restano aperti a lungo —
"cercare i genitori di...", "verificare il secondo matrimonio di...". Il tab
**Task** raccoglie questi propositi accanto al campo di ricerca: sono appunti a
tutti gli effetti, ma tenuti dove servono, cioè vicino allo strumento con cui
si verificano.

### Dati salvati

| Percorso | Contenuto |
| --- | --- |
| `Archivi.json` | Struttura degli archivi esplorati |
| `data/` | Cache locale delle immagini scaricate |
| `manifest/` | Manifest IIIF delle immagini |
| `important/`, `important.json` | Pagine contrassegnate come importanti |
| `important_squized.json` | Indirizzi compressi delle pagine segnate |
| `appunti.txt`, `appuntiCitta.txt` | Appunti per nodo e per città |
| `note.txt`, `note2.txt` | Appunti generali |

I salvataggi avvengono in modo atomico (scrittura su file temporaneo e
successiva sostituzione), per non lasciare file corrotti in caso di
interruzione.

## Struttura del progetto

```
/
├── bin/                   # File binari compilati
├── obj/                   # Oggetti intermedi della compilazione
├── [NomeClassePrincipale] # Classe principale dell'applicazione
├── ...                    # Altri file del progetto
└── README.md              # Questo file
```

## Contribuire

Le contribuzioni sono benvenute! Se desideri contribuire a questo progetto:

1. Fai un fork del repository
2. Crea un branch per la tua feature (`git checkout -b feature/NuovaFunzionalita`)
3. Committa le tue modifiche (`git commit -m 'Aggiunta nuova funzionalità'`)
4. Pusha al branch (`git push origin feature/NuovaFunzionalita`)
5. Apri una Pull Request

## Licenza

[MIT]

## Contatti

Gaetano Lazzo - [@gaelazzo](https://github.com/gaelazzo) — per segnalazioni e richieste usa le [Issues](https://github.com/gaelazzo/antenati/issues)

Link al progetto: [https://github.com/gaelazzo/antenati](https://github.com/gaelazzo/antenati)