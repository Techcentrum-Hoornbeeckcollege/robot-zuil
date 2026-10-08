# Beheer van de zuil

Voor wie de zuil in de hal beheert. Geen programmeerkennis nodig.

## Binnenkomen

Er is geen knop. Dat is bewust: een bezoeker die een knop "Instellingen" op een
zuil in de hal ziet, drukt erop.

1. Houd de **rechteronderhoek** van het scherm **drie seconden** ingedrukt. De
   hoek licht heel zwak op zodra de zuil de druk herkent.
2. Vul het beheerwachtwoord in.

Na vijftien minuten zonder gebruik logt de zuil u uit. Het beheerscherm gaat al
na twee minuten dicht, omdat een openstaand instellingenscherm in een openbare
hal erger is dan een beginscherm.

Vijf keer een verkeerd wachtwoord en inloggen wordt een paar minuten geweigerd.
Dat blijft gelden als iemand de stekker eruit trekt.

## Het wachtwoord

Standaard is het `12345678`. **Verander dit.** Zolang het de standaardwaarde is,
staat er een waarschuwing boven het beheerscherm, en kan iedereen die dit
project kent bij de instellingen.

Het wachtwoord staat niet in de zuil zelf maar op het apparaat, zodat niemand
het vanaf het touchscreen kan wijzigen. Op de Raspberry Pi:

```bash
sudo nano /etc/zuil/zuil.env      # ZUIL_Admin__Password=...
sudo systemctl restart zuil
```

`deploy/install.sh` zet daar bij een nieuwe installatie een willekeurig
wachtwoord in en laat het één keer zien. Bewaar het.

## De tabbladen

### Outlook

De gegevens waarmee de zuil de agenda's ophaalt. U krijgt ze van wie de Entra-
omgeving beheert; zie [outlook-setup.md](outlook-setup.md).

- **Tenant-id** en **Client-id** zijn beide een GUID.
- **Client secret** wordt versleuteld opgeslagen en nooit teruggegeven. Staat er
  een secret, dan ziet u `••••••••` met *Vervangen* en *Wissen*. De zuil kan hem
  u niet laten zien, ook niet als u erom vraagt.
- **Tijdzone** is een IANA-naam, bijvoorbeeld `Europe/Amsterdam`. Bepaalt ook
  wanneer een dag begint op het agendabord.

**Verbinding testen** gebruikt wat is *opgeslagen*, niet wat in het formulier
staat: sla eerst op. De test haalt één vergadering op uit het postvak van de
eerste zaal. Veelvoorkomende uitkomsten:

| Melding | Wat te doen |
|---|---|
| Inloggen bij Entra mislukt (AADSTS7000215) | Secret is verkeerd of verlopen |
| Geen toegang tot dit postvak | Rechten of `ApplicationAccessPolicy` ontbreken |
| Het postvak bestaat niet | Controleer het adres bij Zalen |
| Geen verbinding met Outlook | Netwerk van de Pi |

### Synchronisatie

Hoe vaak en hoe ver de zuil agenda's ophaalt. De standaardwaarden zijn prima;
aanpassen hoeft eigenlijk alleen als er iets misgaat.

**Waarschuwen na** moet ruimer staan dan het interval, anders staat de melding
"gegevens zijn mogelijk niet actueel" permanent aan en leert iedereen die te
negeren. De zuil weigert zo'n combinatie.

**Afgelopen vergaderingen bewaren** kort houden: dit zijn namen van bezoekers op
een apparaat in een openbaar gebouw.

**Nu synchroniseren** doet meteen een ronde. Na elke wijziging hier gebeurt dat
automatisch, dus deze knop is vooral om te kijken of het werkt.

### Zalen

De zalen waar de zuil naar verwijst. Tik op een zaal om hem te wijzigen.

- **Naam** is wat de bezoeker leest.
- **Postvak** is het resource-postvak van de zaal in Outlook. Dit moet exact
  kloppen, anders haalt de zuil er niets op.
- **Looproute** is de tekst die de bezoeker krijgt. Schrijf het zoals u het zou
  zeggen: "Trap op, dan rechts, derde deur."
- **Indicator** is welk lampje op de zuil deze zaal aanwijst. Nummers die een
  andere zaal al gebruikt, kunt u niet kiezen — twee zalen op één indicator
  betekent dat de zuil de helft van de tijd de verkeerde kant op wijst.
- **Id** wordt automatisch uit de naam gemaakt en staat daarna vast, omdat hij
  in opgeslagen gegevens wordt gebruikt.

Wijzigingen gaan in zodra u opslaat; opnieuw opstarten is niet nodig. Het
oorspronkelijke `config/rooms.json` wordt alleen bij de állereerste start
gelezen — het later aanpassen doet niets meer.

### Privacy

**Onderwerpen van vergaderingen** bepaalt of de titel van een vergadering op het
scherm komt.

- **Verborgen** (standaard) — alleen tijd en zaal. Dit is wat de zuil mag laten
  zien zonder dat het persoonsgegevens worden.
- **Alleen beheer** — u ziet de titels als u bent ingelogd. Handig om uit te
  zoeken waarom iemand naar de verkeerde zaal is gestuurd.
- **Iedereen** — de titels komen op een scherm in een openbare hal. Een titel
  zegt vaak meer dan een naam ("Exitgesprek J. Nap"). Overleg dit met wie de
  privacyverklaring van het gebouw beheert.

**Terug naar beginscherm na** is wat de naam van de vorige bezoeker van het
scherm haalt. Niet te ruim zetten.

### Apparaat

De verbinding met de ESP32 in de zuil. Hier hoeft u normaal niets te doen.

Drie velden zijn gemarkeerd met *herstart vereist* — bus, adres en attention-pin
worden alleen bij het opstarten gelezen. Wijzig ze en start daarna de dienst
opnieuw op.

**Indicators testen** stuurt het lampje van één zaal aan, zodat u kunt zien of
de juiste gaat branden. Handig na het verleggen van kabels.

### Weergave

**Beginscherm** is wat de zuil laat zien als er niemand bij staat: *Straks*
(standaard), *Alle agenda's* of het eenvoudige *Welkom*-scherm.

**Agendaknoppen onderaan** uit zetten maakt er weer een zuil van die alleen op
naam zoekt.

### Diagnose

Alleen lezen. Dit is wat u nodig hebt als het scherm in de hal er verkeerd
uitziet.

- **Klok gesynchroniseerd** moet *ja* zijn. De Pi heeft geen batterij-klok, en
  een zuil met een verkeerde tijd stuurt mensen met grote zekerheid naar de
  verkeerde zaal. Staat hier *nee*, dan werkt NTP niet en is dat het eerste om
  op te lossen.
- **Laatst gelukt** hoort binnen een paar minuten te liggen.
- **Laatste foutmelding** is de volledige melding, niet de ingekorte versie die
  bezoekers zouden kunnen zien.

## Als er iets niet werkt

**Het scherm staat op "Outlook is nog niet ingesteld".** De gegevens onder
Outlook ontbreken of zijn onvolledig. De zuil wacht rustig af en gaat door zodra
u opslaat.

**Niemand wordt gevonden, maar de agenda is wel gevuld.** De zuil zoekt op de
volledige naam zoals die in de uitnodiging staat, en op niets minder. Dat is
bewust: zoeken op een deel van een naam zou de zuil in een adresboek van het
hele gebouw veranderen.

**De agenda is leeg en Diagnose meldt een fout.** Lees de foutmelding onder
Diagnose en gebruik daarna *Verbinding testen* onder Outlook.

**De verkeerde indicator gaat aan.** Controleer de indicatornummers onder Zalen
en test ze onder Apparaat.

**Het beheerscherm is onbereikbaar en het wachtwoord is kwijt.** Zet een nieuw
wachtwoord in `/etc/zuil/zuil.env` en start de dienst opnieuw op.

**Een instelling is onherstelbaar verprutst.** `config/rooms.json` en
`config/appsettings.Production.json` op het apparaat zijn alleen nog
startwaarden. De werkelijke instellingen staan in `zuil-cache.db`; die
weggooien zet alles terug op de standaardwaarden — maar daarmee verdwijnt ook de
opgeslagen Outlook-secret, die u dan opnieuw moet invullen.
