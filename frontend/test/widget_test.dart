
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:provider/provider.dart';

import 'package:daily_chess_puzzle/main.dart';
import 'package:daily_chess_puzzle/providers/auth_provider.dart';
import 'package:daily_chess_puzzle/features/splash/screens/splash_page.dart';
import 'package:daily_chess_puzzle/features/navigation/main_navigation_page.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  const geolocatorChannel = MethodChannel(
    'flutter.baseflow.com/geolocator',
  );

  setUp(() {
    // Kayıtlı oturumu olmayan misafir kullanıcı.
    FlutterSecureStorage.setMockInitialValues(
      <String, String>{},
    );

    // Yerel uygulama tercihleri.
    SharedPreferences.setMockInitialValues(
      <String, Object>{},
    );

    // Konum servislerini test ortamında devre dışı bırak.
    TestDefaultBinaryMessengerBinding
        .instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
      geolocatorChannel,
          (MethodCall call) async {
        switch (call.method) {
          case 'isLocationServiceEnabled':
            return false;

          case 'checkPermission':
          case 'requestPermission':
            return 0;

          default:
            return null;
        }
      },
    );
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding
        .instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
      geolocatorChannel,
      null,
    );
  });

  testWidgets(
    'Checkrix uygulaması açılıyor ve ana navigasyona geçiyor',
        (WidgetTester tester) async {
      // Uygulamayı başlat.
      await tester.pumpWidget(
        const DailyChessPuzzleApp(),
      );

      await tester.pump();

      // MaterialApp kontrolü.
      expect(find.byType(MaterialApp), findsOneWidget);

      final materialApp = tester.widget<MaterialApp>(
        find.byType(MaterialApp),
      );

      expect(materialApp.title, 'Checkrix');

      // Splash ekranı görüntülenmeli.
      expect(find.byType(SplashPage), findsOneWidget);
      expect(find.byType(NavigationBar), findsNothing);

      // AuthProvider başlangıç işlemini doğrula.
      final splashContext = tester.element(
        find.byType(SplashPage),
      );

      final authProvider =
      splashContext.read<AuthProvider>();

      await tester.pump();

      // Minimum splash süresi dolmadan ana ekran açılmamalı.
      await tester.pump(
        const Duration(milliseconds: 1000),
      );

      expect(find.byType(SplashPage), findsOneWidget);
      expect(find.byType(NavigationBar), findsNothing);

      // Minimum splash süresini tamamla.
      await tester.pump(
        const Duration(milliseconds: 800),
      );

      // Başlangıç işlemlerinin tamamlanmasını bekle.
      for (var i = 0; i < 50; i++) {
        if (find.byType(MainNavigationPage)
            .evaluate()
            .isNotEmpty) {
          break;
        }

        await tester.pump(
          const Duration(milliseconds: 100),
        );
      }

      // Sayfa geçiş animasyonunu tamamla.
      await tester.pump();
      await tester.pump(
        const Duration(milliseconds: 500),
      );
      await tester.pump();

      // Ana ekran kontrolü.
      expect(
        find.byType(MainNavigationPage),
        findsOneWidget,
      );

      expect(find.byType(SplashPage), findsNothing);
      expect(find.byType(NavigationBar), findsOneWidget);

      // Kullanıcı misafir olarak açılmalı.
      expect(authProvider.isLoggedIn, isFalse);
      expect(authProvider.userId, isNull);

      // Navigasyon sekmeleri.
      expect(find.text('Ana Sayfa'), findsOneWidget);
      expect(find.text('Bulmaca'), findsOneWidget);
      expect(find.text('Sıralama'), findsOneWidget);
      expect(find.text('Profil'), findsOneWidget);

      // Widget ağacını güvenli şekilde kaldır.
      await tester.pumpWidget(
        const SizedBox.shrink(),
      );

      await tester.pump();
    },
  );
}
