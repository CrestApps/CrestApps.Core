using CrestApps.Core.AI.Documents.Pdf.Analysis;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class PdfLanguageDetectorTests
{
    [Theory]
    [InlineData("en", "The committee reviewed the report and agreed that the results were better than expected for the year, which was welcomed by all of the members who attended.")]
    [InlineData("fr", "Le comité a examiné le rapport et il a conclu que les résultats de l'année sont meilleurs que prévu, ce qui a été salué par tous les membres présents à la réunion.")]
    [InlineData("de", "Der Ausschuss hat den Bericht geprüft und ist zu dem Ergebnis gekommen, dass die Zahlen für das Jahr besser sind als erwartet, was von allen Mitgliedern begrüßt wurde.")]
    [InlineData("es", "El comité revisó el informe y concluyó que los resultados del año son mejores de lo esperado, lo que fue celebrado por todos los miembros que estaban en la reunión.")]
    [InlineData("it", "Il comitato ha esaminato la relazione e ha concluso che i risultati dell'anno sono migliori del previsto, e questo è stato accolto con favore da tutti i membri presenti.")]
    [InlineData("pt", "O comitê analisou o relatório e concluiu que os resultados do ano são melhores do que o esperado, o que foi comemorado por todos os membros que estavam na reunião.")]
    [InlineData("nl", "De commissie heeft het rapport bekeken en is tot de conclusie gekomen dat de resultaten van het jaar beter zijn dan verwacht, wat door alle leden werd toegejuicht.")]
    [InlineData("sv", "Kommittén har granskat rapporten och kommit fram till att resultatet för året är bättre än väntat, vilket välkomnades av alla medlemmar som var på mötet.")]
    [InlineData("pl", "Komisja przejrzała raport i uznała, że wyniki za ten rok są lepsze niż oczekiwano, co zostało przyjęte z zadowoleniem przez wszystkich członków, którzy byli na spotkaniu.")]
    [InlineData("tr", "Komite raporu inceledi ve yılın sonuçlarının beklenenden daha iyi olduğu sonucuna vardı, bu da toplantıya katılan bütün üyeler için çok sevindirici bir haber oldu.")]
    [InlineData("fi", "Valiokunta kävi raportin läpi ja totesi, että vuoden tulokset ovat odotettua parempia, mikä oli hyvä uutinen kaikille jäsenille, jotka olivat mukana kokouksessa.")]
    [InlineData("id", "Komite telah meninjau laporan tersebut dan menyimpulkan bahwa hasil tahun ini lebih baik dari yang diharapkan, dan hal itu disambut baik oleh semua anggota yang hadir.")]
    [InlineData("vi", "Ủy ban đã xem xét báo cáo và kết luận rằng kết quả của năm nay tốt hơn so với dự kiến, và điều đó được tất cả các thành viên có mặt trong cuộc họp hoan nghênh.")]
    public void Detect_LatinScript_TellsTheLanguageFromItsCommonWords(string expected, string text)
    {
        var detection = PdfLanguageDetector.Detect(text);

        Assert.Equal(expected, detection.Code);
        Assert.Equal("Latin", detection.Script);
        Assert.True(detection.Confidence > 0.2, $"Confidence {detection.Confidence} for {expected}.");
    }

    [Theory]
    [InlineData("ru", "Комитет рассмотрел доклад и пришёл к выводу, что результаты года лучше, чем ожидалось.")]
    [InlineData("uk", "Комітет розглянув доповідь і дійшов висновку, що результати року кращі, ніж очікувалося.")]
    [InlineData("el", "Η επιτροπή εξέτασε την έκθεση και κατέληξε ότι τα αποτελέσματα της χρονιάς είναι καλύτερα.")]
    [InlineData("ar", "راجعت اللجنة التقرير وخلصت إلى أن نتائج العام أفضل من المتوقع وقد رحب بها جميع الأعضاء.")]
    [InlineData("he", "הוועדה בחנה את הדוח והגיעה למסקנה שתוצאות השנה טובות מהצפוי, וכל החברים בירכו על כך.")]
    [InlineData("hi", "समिति ने रिपोर्ट की समीक्षा की और निष्कर्ष निकाला कि वर्ष के परिणाम अपेक्षा से बेहतर हैं।")]
    [InlineData("th", "คณะกรรมการได้ตรวจสอบรายงานและสรุปว่าผลลัพธ์ของปีนี้ดีกว่าที่คาดไว้")]
    [InlineData("ko", "위원회는 보고서를 검토하고 올해의 결과가 예상보다 좋다고 결론을 내렸습니다.")]
    [InlineData("ja", "委員会は報告書を検討し、今年の結果は予想よりも良いと結論付けました。")]
    [InlineData("zh", "委员会审查了报告，并得出结论，今年的结果比预期的要好，所有成员都表示欢迎。")]
    public void Detect_OtherScripts_TellsTheLanguageFromItsScript(string expected, string text)
    {
        var detection = PdfLanguageDetector.Detect(text);

        Assert.Equal(expected, detection.Code);
        Assert.True(detection.Confidence > 0.3, $"Confidence {detection.Confidence} for {expected}.");
    }

    [Fact]
    public void Detect_TooLittleText_IsUndetermined()
    {
        var detection = PdfLanguageDetector.Detect("12 34 — ok");

        Assert.Equal("und", detection.Code);
        Assert.Equal(0, detection.Confidence);
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("nb-NO", "no")]
    [InlineData("FR", "fr")]
    [InlineData("zh_Hant", "zh")]
    public void PrimaryTag_ReadsTheLanguageSubtag(string tag, string expected)
    {
        Assert.Equal(expected, PdfLanguageDetector.PrimaryTag(tag));
    }
}
