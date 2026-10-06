# TS6 Overlay

Nakładka na grę dla **TeamSpeak 6**: pokazuje, kto jest na Twoim kanale i kto mówi, a także kto wchodzi, kto wychodzi, wiadomości i szturchnięcia. Ostrzega też, gdy mówisz do wyciszonego mikrofonu.

![Motywy](motywy.png)

## Instalacja

1. Pobierz `TS6Overlay.exe` ze strony [Releases](https://github.com/aki76pl/ts6-overlay/releases/latest).
2. Uruchom plik i kliknij **Tak**, gdy program zaproponuje instalację. Instalacja nie wymaga uprawnień administratora. Program trafia do `%LOCALAPPDATA%\Programs\TS6Overlay`, dostaje skrót w menu Start i uruchamia się razem z Windows.
3. W TeamSpeaku kliknij **Zezwól**, gdy pojawi się prośba o dostęp dla „TS6 Overlay”. Jeśli prośba się nie pojawi, sprawdź w TS **Ustawienia → Remote Apps**, czy API jest włączone.

Program sam sprawdza aktualizacje i instaluje je jednym kliknięciem. Odinstalujesz go w **Ustawieniach Windows → Aplikacje**.

## Funkcje

| | |
|---|---|
| **Kto mówi** | lista osób na kanale; mówiący ma podświetlony wiersz i świecący znacznik, szept ma inny kolor |
| **Wejścia i wyjścia** | powiadomienie i sygnał, gdy ktoś wchodzi na kanał lub go opuszcza |
| **Bindy (soundboard)** | własne skróty klawiszowe (np. F9, Ctrl+1, Num 5), które odtwarzają wybrany dźwięk, również w grze; dźwięk może iść na osobne urządzenie, np. wirtualny kabel |
| **Własne dźwięki** | do każdego zdarzenia przypiszesz swój plik `.wav` lub `.mp3` (przycisk albo przeciągnij i upuść) |
| **Wiadomości i szturchnięcia** | prywatne, z kanału i z serwera (każdy rodzaj włączasz osobno); szturchnięcie trzęsie powiadomieniem |
| **Mikrofon wyciszony** | czerwony pasek, gdy masz wyciszony mikrofon lub głośniki; gdy mówisz do wyciszonego mikrofonu, pasek miga i słychać sygnał |
| **Autoukrywanie** | gdy nikt nie mówi, nakładka blednie albo zostaje sam nagłówek; wraca od razu, gdy coś się dzieje |
| **Tylko w grach** | nakładka widoczna tylko wtedy, gdy gra z listy (albo dowolna aplikacja pełnoekranowa) jest na pierwszym planie |
| **Ulubieni i ignorowani** | ulubieni mają własny kolor nicku i dźwięk wejścia; ignorowani (np. boty muzyczne) znikają z nakładki |
| **Motywy** | 8 wbudowanych motywów (domyślny Terminal) i edytor do tworzenia własnych; motywy wymieniasz ze znajomymi w plikach `.ts6theme` |
| **OBS** | ta sama nakładka jako strona `http://localhost:5898/` dla źródła „Przeglądarka”; działa też w grach pełnoekranowych |
| **Statystyki sesji** | kto ile mówił, ile razy wchodził, historia zdarzeń, eksport do CSV |
| **Nazwy kanałów** | usuwa znaczniki `[spacer]` i ozdobniki, np. `[spacer]╟-● Pluton 9` → `Pluton 9` |

## Obsługa

- **Ctrl + lewy przycisk myszy** na nakładce: przesuwanie.
- **Ctrl+Shift+O**: pokaż lub ukryj nakładkę.
- **Ikona w zasobniku**: dwuklik otwiera ustawienia, prawy przycisk otwiera szybkie menu (motyw, dźwięki, tryby, statystyki).
- Ponowne uruchomienie programu, np. ze skrótu w menu Start, otwiera ustawienia.

### Własne dźwięki

W **Ustawieniach → Powiadomienia** każde zdarzenie (wejście, wyjście, wiadomość, szturchnięcie, wejście ulubionego, mówienie do wyciszonego mikrofonu) ma przycisk **Wybierz…**. Plik możesz też przeciągnąć na wiersz zdarzenia. Program przyjmuje formaty `.wav`, `.mp3`, `.wma`, `.aiff` i `.m4a`, kopiuje plik do `%APPDATA%\TS6Overlay\sounds` i odtwarza najwyżej 8 sekund. Przycisk **Wbudowany** przywraca domyślny sygnał. Każdy ulubiony może mieć własny dźwięk wejścia, który ustawiasz w zakładce **Osoby**.

### Bindy

W **Ustawieniach → Bindy** kliknij **+ Dodaj bind**, potem przycisk skrótu, i naciśnij klawisz albo kombinację (Esc anuluje, Backspace usuwa). Dźwięk wybierasz przyciskiem albo przeciągasz plik na wiersz binda. Ponowne naciśnięcie skrótu w trakcie odtwarzania zatrzymuje dźwięk. Bindy są też w menu ikony w zasobniku.

Domyślnie dźwięki bindów słyszysz tylko Ty. Żeby słyszeli je inni na TeamSpeaku, zainstaluj wirtualny kabel audio (np. VB-Audio Virtual Cable), wybierz go w polu **Urządzenie** i podaj do TS razem z mikrofonem (np. przez VoiceMeeter).

### OBS

W ustawieniach w zakładce **OBS** włącz stronę, a potem w OBS dodaj źródło **Przeglądarka** z adresem `http://localhost:5898/` (szerokość 500, wysokość 600). Do adresu możesz dopisać parametry:

- `?only=talking`: tylko osoby, które mówią,
- `?scale=1.5`: powiększenie,
- `?notices=0`: bez powiadomień.

## Ograniczenia

- Okienko nakładki na ekranie widać, gdy gra działa w trybie **okno bez ramek** (Borderless). W wyłącznym pełnym ekranie gra je zasłania. Wtedy zostaje nakładka w OBS.
- Program korzysta z lokalnego API TeamSpeaka (Remote Apps, `ws://127.0.0.1:5899`). Może czytać, co się dzieje, ale nie może np. przenosić ani wyciszać innych osób.
- Wykrywanie mowy przy wyciszonym mikrofonie nasłuchuje domyślnego mikrofonu **tylko wtedy, gdy jest on wyciszony w TS**. Program bada jedynie poziom głośności i nigdzie nie zapisuje dźwięku.

## Budowanie

Wymagany .NET 10 SDK.

```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Plik `publish\TS6Overlay.exe` działa bez instalowania .NET.

## Licencja

MIT
