using Gedcom;
using GedcomParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace viewer {

    /// <summary>Opzioni di presentazione delle persone (le caselle della scheda di ricerca)</summary>
    public sealed class DescriptionOptions {
        /// <summary>tutti i nomi di battesimo, non solo il primo</summary>
        public bool FullNames;
        /// <summary>date complete, non solo l'anno</summary>
        public bool FullDates;
        /// <summary>coniugi nella riga breve e nella scheda</summary>
        public bool ShowSpouse;
        /// <summary>identificativo in testa alla riga breve</summary>
        public bool ShowKey;
        /// <summary>identificativi anche di genitori, coniugi e figli nella scheda</summary>
        public bool ShowAllKeys;
    }

    /// <summary>
    /// Archivio genealogico GEDCOM: caricamento, ricerca e descrizione delle persone.
    /// Nasconde tre decisioni: la libreria GEDCOM e il suo modello (nessun tipo Gedcom esce di qui),
    /// il linguaggio di ricerca ("nome cognome DI padre DI nonno E DI madre DI suo padre marito di/
    /// moglie di coniuge", "?" come jolly, prefissi come "del", nome e cognome invertiti) e il formato
    /// delle descrizioni. Le opzioni di presentazione arrivano dal chiamante (DescriptionOptions).
    /// </summary>
    public sealed class GedcomSearch {
        readonly GedcomDatabase database;

        public string FileName { get; }

        GedcomSearch(string fileName, GedcomDatabase database) {
            FileName = fileName;
            this.database = database;
        }

        /// <summary>Legge il file; null se non e' un GEDCOM valido</summary>
        public static GedcomSearch Load(string fileName) {
            var r = new GedcomRecordReader();
            if (!r.ReadGedcom(fileName))
                return null;
            var db = r.Database;
            db.BuildSurnameList();
            return new GedcomSearch(fileName, db);
        }

        /// <summary>Persone che corrispondono alla ricerca, una riga breve ciascuna</summary>
        public IReadOnlyList<string> Search(string query, DescriptionOptions o) {
            var f = new Formatter(database, o);
            return findIndividual(query).Select(f.SimpleDescr).ToList();
        }

        /// <summary>Scheda lunga della persona (coniugi, genitori, figli); id con o senza "XREF"; null se non c'e'</summary>
        public string Describe(string id, DescriptionOptions o) {
            string xref = id?.Trim();
            if (string.IsNullOrEmpty(xref))
                return null;
            if (!xref.StartsWith("XREF"))
                xref = "XREF" + xref;
            var indi = findByXref(xref);
            return indi == null ? null : new Formatter(database, o).LongDescr(indi);
        }

        GedcomIndividualRecord findByXref(string xref) {
            try {
                return database[xref] as GedcomIndividualRecord;
            }
            catch { return null; }
        }

        /// <summary>Anno di una data GEDCOM ("12 MAR 1820", "ABT 1790" -> ultima parola)</summary>
        internal static string getYear(string D) {
            if (String.IsNullOrEmpty(D))
                return "";
            var parts = D.Split(' ');
            return parts[parts.Length - 1];
        }

        // ---------- linguaggio di ricerca ----------

        internal static bool isPrefix(string S) {
            if (string.IsNullOrEmpty(S))
                return false;
            if (S.Length < 3)
                return true;
            if (S.Length > 3)
                return false;
            S = S.ToLower();
            if (S == "del")
                return true;
            return false;
        }

        internal static string getSurname(string s) {
            if (s == null)
                return null;
            var parts = s.Trim().Split(' ');
            if (parts.Length == 0)
                return "";
            if (parts[0] == "?") {
                int i = s.IndexOf("?");
                return s.Substring(i + 1).Trim();
            }
            if (parts[parts.Length - 1] == "?")
                return "?";

            if (parts.Length > 1 && isPrefix(parts[parts.Length - 2])) {
                return parts[parts.Length - 2].Trim() + " " + parts[parts.Length - 1].Trim();
            }

            if (parts.Length > 1)
                return parts[parts.Length - 1].Trim();
            return s.Trim();
        }

        internal static string getNames(string s) {
            if (s == null)
                return null;
            var parts = s.Trim().Split(' ').ToList();

            if (parts.Count == 0)
                return "";
            if (parts[0] == "?")
                return "?";

            if (parts.Count > 1 && isPrefix(parts[parts.Count - 2])) {
                parts.RemoveRange(parts.Count - 2, 2);
            }
            else if (parts.Count > 1) {
                parts.RemoveAt(parts.Count - 1);
            }
            else if (parts.Count == 1) {
                return "?";
            }
            return String.Join(" ", parts);
        }

        //Michele, Maria Sparano DI Giuseppe di Filippo E DI Maria Concetta marito di Francesca Giovanna Adelmi
        //Michele, Maria Sparano DI Giuseppe di Filippo E DI Maria Concetta moglie di Francesco Giovanna Adelmi
        IEnumerable<GedcomIndividualRecord> findIndividual(string searchMask) {
            string name = null;
            string surname = null;
            string fatherName = null;
            string grandFatherName = null;
            string motherFatherName = null;
            string motherNameSurname = null;
            string wifeNameSurname = null;
            if (searchMask.StartsWith("DI")) {
                searchMask = " " + searchMask;
            }

            var coniugeDi = searchMask.Split(new string[] { " marito di ", " moglie di " }, StringSplitOptions.None);
            if (coniugeDi.Length > 1) {
                wifeNameSurname = coniugeDi[1]?.Trim();
                searchMask = coniugeDi[0]?.Trim();
            }
            var motherParts = searchMask.Split(new string[] { " E DI " }, StringSplitOptions.None);
            if (motherParts.Length > 1) {
                motherNameSurname = motherParts[1]?.Trim();
                searchMask = motherParts[0];
                var tryFatherMothers = motherNameSurname.Split(new string[] { " DI " }, StringSplitOptions.None);
                if (tryFatherMothers.Length > 1) {
                    motherFatherName = tryFatherMothers[1]?.Trim();
                    motherNameSurname = tryFatherMothers[0]?.Trim();
                }
            }

            var fatherParts = searchMask.Split(new string[] { " DI " }, StringSplitOptions.None);
            if (fatherParts.Length > 1) {
                fatherName = fatherParts[1]?.Trim();
                searchMask = fatherParts[0]?.Trim();
                if (fatherParts.Length > 2) {
                    grandFatherName = fatherParts[2]?.Trim();
                }
            }


            name = getNames(searchMask)?.Trim();
            surname = getSurname(searchMask)?.Trim();

            var mainParts = searchMask.Split(' ');
            if (mainParts.Length == 1 && fatherName != null) {
                //verifica se ha messo il cognome nel padre, sistema le cose
                var fatherNameTry = getNames(fatherName)?.Trim();
                var fatherSurnameTry = getSurname(fatherName)?.Trim();
                if (fatherNameTry != "?" && fatherSurnameTry != "?") {
                    name = surname;
                    surname = fatherSurnameTry;
                    fatherName = fatherNameTry;
                }


            }

            if (surname != null && surname != "?" && !database.Surnames.ContainsKey(surname) && name == null) {
                name = surname;
                surname = "?";
            }

            if (surname != null && surname != "?" && !database.Surnames.ContainsKey(surname) && name != null && database.Surnames.ContainsKey(name)) {
                var x = name;
                name = surname;
                surname = x;
            }

            string motherName = motherNameSurname != null ? getNames(motherNameSurname)?.Trim() : null;
            string motherSurname = motherNameSurname != null ? getSurname(motherNameSurname)?.Trim() : null;
            if (motherSurname != null && motherSurname != "?" && !database.Surnames.ContainsKey(motherSurname) && motherName == "?") {
                motherName = motherSurname;
                motherSurname = "?";
            }
            if (motherSurname != null && motherSurname != "?" && motherName != null && motherName != "?") {
                if (!database.Surnames.ContainsKey(motherSurname) && database.Surnames.ContainsKey(motherName)) {
                    var x = motherName;
                    motherName = motherSurname;
                    motherSurname = x;
                }
            }

            string wifeName = wifeNameSurname != null ? getNames(wifeNameSurname)?.Trim() : null;
            string wifeSurname = wifeNameSurname != null ? getSurname(wifeNameSurname)?.Trim() : null;
            if (wifeSurname != null && wifeSurname != "?" && !database.Surnames.ContainsKey(wifeSurname) && wifeName == "?") {
                wifeName = wifeSurname;
                wifeSurname = "?";
            }
            if (wifeSurname != null && wifeSurname != "?" && wifeName != null && wifeName != "?") {
                if (!database.Surnames.ContainsKey(wifeSurname) && database.Surnames.ContainsKey(wifeName)) {
                    var x = wifeName;
                    wifeName = wifeSurname;
                    wifeSurname = x;
                }
            }

            foreach (var indi in database.Individuals) {
                if (!String.IsNullOrEmpty(name) && name != "?") {
                    if (!indi.MatchFirstname(name, false))
                        continue;
                }
                if (!String.IsNullOrEmpty(surname) && surname != "?") {
                    if (!indi.MatchSurname(surname, false))
                        continue;
                }

                if (wifeNameSurname != null) {
                    bool foundWife = false;
                    foreach (var spouse in Formatter.getSpouses(database, indi)) {
                        if (wifeName != null & wifeName != "?") {
                            if (!spouse.MatchFirstname(wifeName, false))
                                continue;
                        }
                        if (wifeSurname != null & wifeSurname != "?") {
                            if (!spouse.MatchSurname(wifeSurname, false))
                                continue;
                        }
                        foundWife = true;
                        break;
                    }
                    if (!foundWife)
                        continue;
                }

                if (fatherName != null) {
                    bool foundFather = false;
                    foreach (var childFam in indi.ChildIn) {
                        var fam = (GedcomFamilyRecord)database[childFam.Family];
                        if (fam.Husband != null) {
                            var FatherIndi = (GedcomIndividualRecord)database[fam.Husband];

                            if (fatherName != null & fatherName != "?") {
                                if (!FatherIndi.MatchFirstname(fatherName, false))
                                    continue;
                            }

                            if (grandFatherName != null) { //Cerca il nome del padre del padre
                                if (FatherIndi.ChildIn == null || FatherIndi.ChildIn.Count == 0)
                                    continue;
                                var grandfam = (GedcomFamilyRecord)database[FatherIndi.ChildIn[0].Family];
                                if (grandfam.Husband == null)
                                    continue;
                                var grandFatherIndi = (GedcomIndividualRecord)database[grandfam.Husband];
                                if (!grandFatherIndi.MatchFirstname(grandFatherName, false))
                                    continue;
                            }

                            foundFather = true;
                            break;
                        }
                    }
                    if (!foundFather)
                        continue;
                }

                if (motherNameSurname != null) {
                    bool foundMother = false;
                    foreach (var childFam in indi.ChildIn) {
                        var fam = (GedcomFamilyRecord)database[childFam.Family];
                        if (fam.Wife != null) {
                            var MotherIndi = (GedcomIndividualRecord)database[fam.Wife];
                            if (motherName != null & motherName != "?") {
                                if (!MotherIndi.MatchFirstname(motherName, false))
                                    continue;
                            }
                            if (motherSurname != null & motherSurname != "?") {
                                if (!MotherIndi.MatchSurname(motherSurname, false))
                                    continue;
                            }

                            if (motherFatherName != null) {//Cerca il nome del padre della madre
                                if (MotherIndi.ChildIn == null || MotherIndi.ChildIn.Count == 0)
                                    continue;
                                var grandfam = (GedcomFamilyRecord)database[MotherIndi.ChildIn[0].Family];
                                if (grandfam.Husband == null)
                                    continue;
                                var grandFatherIndi = (GedcomIndividualRecord)database[grandfam.Husband];
                                if (!grandFatherIndi.MatchFirstname(motherFatherName, false))
                                    continue;
                            }


                            foundMother = true;
                            break;
                        }
                    }
                    if (!foundMother)
                        continue;
                }


                yield return indi;
            }
        }

        // ---------- formato delle descrizioni ----------

        /// <summary>Descrizioni di persone con un certo insieme di opzioni</summary>
        sealed class Formatter {
            readonly GedcomDatabase database;
            readonly DescriptionOptions o;

            public Formatter(GedcomDatabase database, DescriptionOptions o) {
                this.database = database;
                this.o = o ?? new DescriptionOptions();
            }

            string shortenName(string name) {
                if (o.FullNames)
                    return name;
                var parts = name.Split(' ', ',');
                return parts[0];
            }

            string getName(GedcomIndividualRecord indi) {
                if (indi == null)
                    return "";
                if (indi.Names.Count > 0) {
                    var name = indi.Names[0];
                    return shortenName(name.Given);
                }
                return "no name";
            }

            string getNameSurname(GedcomIndividualRecord indi) {
                if (indi == null)
                    return "";
                if (indi.Names.Count > 0) {
                    var name = indi.Names[0];
                    if (String.IsNullOrEmpty(name.SurnamePrefix)) {
                        return String.Join(" ", new string[] { shortenName(name.Given), name.Surname });
                    }
                    else {
                        return String.Join(" ", new string[] { shortenName(name.Given), name.SurnamePrefix, name.Surname });
                    }
                }
                return "no name";
            }

            internal static IEnumerable<GedcomIndividualRecord> getSpouses(GedcomDatabase database, GedcomIndividualRecord indi) {
                foreach (var spouse in indi.SpouseIn) {
                    var famID = spouse.Family;
                    if (famID is null)
                        continue;
                    var fam = (GedcomFamilyRecord)database[famID];
                    if (fam.Husband != null && fam.Husband != indi.XRefID)
                        yield return database[fam.Husband] as GedcomIndividualRecord;
                    if (fam.Wife != null && fam.Wife != indi.XRefID)
                        yield return database[fam.Wife] as GedcomIndividualRecord;
                }
            }

            string getMarriageDate(GedcomFamilyRecord fam, bool recursive) {
                if (fam.Marriage == null)
                    return "";
                string married = getYear(fam.Marriage.Date?.DateString);
                if (String.IsNullOrEmpty(married))
                    return "";
                if (recursive) {
                    return " married in " + married;
                }
                else {
                    return "[" + married + "]";
                }
            }

            string getMarriage(GedcomIndividualRecord indi1, GedcomIndividualRecord indi2, bool recursive) {
                foreach (var spouse in indi1.SpouseIn) {
                    var famID = spouse.Family;
                    if (famID is null)
                        continue;
                    var fam = (GedcomFamilyRecord)database[famID];
                    if (fam.Husband != null && fam.Husband == indi2.XRefID)
                        return getMarriageDate(fam, recursive);
                    if (fam.Wife != null && fam.Wife == indi2.XRefID)
                        return getMarriageDate(fam, recursive);
                }
                return "";
            }

            string getBornDeath(GedcomIndividualRecord indi) {
                string S = "";
                if (indi.Birth != null && indi.Birth.Date != null) {
                    S += o.FullDates ? indi.Birth.Date.DateString : getYear(indi.Birth.Date.DateString);
                }
                if (indi.Death != null && indi.Death.Date != null) {
                    if (S != "")
                        S += "-";
                    S += o.FullDates ? indi.Death.Date.DateString : getYear(indi.Death.Date.DateString);
                }
                if (S != "")
                    S = $"({S})";
                return S;
            }

            string WifeOrHusband(GedcomIndividualRecord indi, bool recursive) {
                string S = "";
                foreach (var spouse in getSpouses(database, indi)) {
                    if (spouse.Sex == GedcomSex.Male) {
                        S += ", moglie di ";
                    }
                    else {
                        S += ", marito di ";
                    }
                    S += getNameSurname(spouse) + getBornDeath(spouse);
                    if (o.ShowAllKeys && recursive) {
                        S += " " + spouse.XRefID + " ";
                    }
                    S += getMarriage(indi, spouse, recursive);
                }
                return S;
            }

            string parents(GedcomIndividualRecord indi, bool recursive) {
                string S = "";
                foreach (var childFam in indi.ChildIn) {
                    if (recursive) {
                        S = S + "\r\n";
                    }
                    var fam = (GedcomFamilyRecord)database[childFam.Family];
                    if (fam.Husband != null) {
                        var HusbandIndi = (GedcomIndividualRecord)database[fam.Husband];
                        if (recursive)
                            S += "    ";
                        S += " DI " + getName(HusbandIndi) + getBornDeath(HusbandIndi);
                        if (o.ShowAllKeys && recursive) {
                            S += " " + HusbandIndi.XRefID + " ";
                        }
                    }
                    if (fam.Wife != null) {
                        var WifeIndi = (GedcomIndividualRecord)database[fam.Wife];
                        string wifeName = getNameSurname(WifeIndi).Trim();
                        if (String.IsNullOrEmpty(wifeName))
                            continue;

                        if (fam.Husband != null) {
                            S += " E";
                        }
                        else {
                            if (recursive) S += "    ";
                        }
                        S += " DI " + wifeName + getBornDeath(WifeIndi);
                        if (o.ShowAllKeys && recursive) {
                            S += " " + WifeIndi.XRefID + " ";
                        }
                    }
                    S += getMarriageDate(fam, recursive);
                    if (recursive && fam.Husband != null) {
                        S += parents((GedcomIndividualRecord)database[fam.Husband], true);
                    }
                }
                return S;
            }

            string children(GedcomIndividualRecord indi) {
                string S = "";
                foreach (var childFam in indi.SpouseIn) {
                    var fam = (GedcomFamilyRecord)database[childFam.Family];
                    if (fam.Husband != null && fam.Husband != indi.XRefID) {
                        var HusbandIndi = (GedcomIndividualRecord)database[fam.Husband];
                        S += "\r\n with " + getNameSurname(HusbandIndi) + ":\r\n";
                    }
                    if (fam.Wife != null && fam.Wife != indi.XRefID) {
                        var WifeIndi = (GedcomIndividualRecord)database[fam.Wife];
                        S += "\r\n with " + getNameSurname(WifeIndi) + ":\r\n";
                    }
                    foreach (var chId in fam.Children) {
                        var child = (GedcomIndividualRecord)database[chId];
                        S += "    ";
                        S += getIndiName(child);
                        if (o.ShowAllKeys) {
                            S += " " + child.XRefID + " ";
                        }
                        S += getBornDeath(child);
                        S += WifeOrHusband(child, true);
                        S += "\r\n";
                    }
                    if (fam.Children.Count == 0) {
                        S += "    no Children";
                        S += "\r\n";
                    }
                }
                return S;
            }

            string getIndiName(GedcomIndividualRecord indi) {
                string S = "";
                foreach (var n in indi.Names) {
                    if (String.IsNullOrEmpty(n.SurnamePrefix)) {
                        S += shortenName(n.Given) + " " + n.Surname;
                    }
                    else {
                        S += shortenName(n.Given) + " " + n.SurnamePrefix + " " + n.Surname;
                    }
                }
                return S;
            }

            public string SimpleDescr(GedcomIndividualRecord indi) {
                string S = "";
                if (o.ShowKey) {
                    S += indi.XRefID + " ";
                }
                S += getIndiName(indi);
                S += getBornDeath(indi);
                S += parents(indi, false);
                if (o.ShowSpouse) {
                    S += WifeOrHusband(indi, false);
                }
                return S;
            }

            public string LongDescr(GedcomIndividualRecord indi) {
                string S = "";

                S += getIndiName(indi);
                S += getBornDeath(indi);
                if (o.ShowAllKeys) {
                    S += "     " + indi.XRefID;
                }
                S += "\n\r";
                if (o.ShowSpouse) {
                    S += WifeOrHusband(indi, true);
                    S += "\n\r";
                }
                S += parents(indi, true);
                S += "\n\r";
                S += children(indi);

                return S;
            }
        }
    }
}
