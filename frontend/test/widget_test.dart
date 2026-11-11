
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:daily_chess_puzzle/main.dart';

void main() {
  testWidgets(
    'Daily Chess Puzzle uygulaması açılıyor',
        (WidgetTester tester) async {
      // Uygulamayı başlat.
      await tester.pumpWidget(const DailyChessPuzzleApp());

      // Sürekli animasyonların bitmesini beklemeden ekranı çiz.
      await tester.pump(const Duration(milliseconds: 500));

      // Ana uygulama kontrolü.
      expect(find.byType(MaterialApp), findsOneWidget);

      // Alt navigasyon kontrolü.
      expect(find.byType(NavigationBar), findsOneWidget);

      // Navigasyon sekmeleri.
      expect(find.text('Ana Sayfa'), findsOneWidget);
      expect(find.text('Bulmaca'), findsOneWidget);
      expect(find.text('Sıralama'), findsOneWidget);
      expect(find.text('Profil'), findsOneWidget);
    },
  );
}
