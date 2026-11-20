
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/auth_provider.dart';
import '../../providers/leaderboard_provider.dart';
import '../../core/services/leaderboard_signalr_service.dart';

import '../home/screens/home_page.dart';
import '../puzzle/screens/puzzle_page.dart';
import '../leaderboard/screens/leaderboard_page.dart';
import '../profile/screens/profile_page.dart';

class MainNavigationPage extends StatefulWidget {
  const MainNavigationPage({super.key});

  @override
  State<MainNavigationPage> createState() =>
      _MainNavigationPageState();
}

class _MainNavigationPageState extends State<MainNavigationPage> {
  int _selectedIndex = 0;

  final GlobalKey<HomePageState> _homePageKey =
  GlobalKey<HomePageState>();

  final GlobalKey<PuzzlePageState> _puzzlePageKey =
  GlobalKey<PuzzlePageState>();

  final GlobalKey<LeaderboardPageState> _leaderboardPageKey =
  GlobalKey<LeaderboardPageState>();

  final GlobalKey<ProfilePageState> _profilePageKey =
  GlobalKey<ProfilePageState>();

  // Tek bir SignalR servis örneği kullanıyoruz.
  final LeaderboardSignalRService _signalRService =
  LeaderboardSignalRService();

  // Eş zamanlı SignalR bağlantı güncellemelerini önler.
  Future<void>? _signalRUpdateFuture;
  AuthProvider? _observedAuthProvider;
  bool _lastAuthState = false;

  late final List<Widget> _pages = [
    HomePage(
      key: _homePageKey,
      onOpenPuzzle: () => _navigateTo(1),
      onOpenLeaderboard: () => _navigateTo(2),
    ),
    PuzzlePage(key: _puzzlePageKey),
    LeaderboardPage(key: _leaderboardPageKey),
    ProfilePage(
      key: _profilePageKey,
      onAuthChanged: _handleAuthChanged,
    ),
  ];

  @override
  void initState() {
    super.initState();

    // SignalR bildirim callback'lerini tanımla.
    _signalRService.onLeaderboardUpdated = () {
      debugPrint(
        'SIGNALR CALLBACK: onLeaderboardUpdated çalıştı.',
      );

      _refreshLeaderboard();
    };

    _signalRService.onConnectionRestored =
        _refreshLeaderboard;

    // Uygulama açıldığında oturum durumunu kontrol et.
    _updateSignalRConnection();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final provider = context.read<AuthProvider>();
    if (identical(provider, _observedAuthProvider)) return;
    _observedAuthProvider?.removeListener(_handleAuthProviderChanged);
    _observedAuthProvider = provider;
    _lastAuthState = provider.isLoggedIn;
    provider.addListener(_handleAuthProviderChanged);
  }

  void _handleAuthProviderChanged() {
    final provider = _observedAuthProvider;
    if (provider == null) return;
    final isLoggedIn = provider.isLoggedIn;
    final sessionExpired = _lastAuthState && !isLoggedIn;
    _lastAuthState = isLoggedIn;
    if (!sessionExpired || !mounted) return;

    setState(() => _selectedIndex = 3);
    unawaited(_handleAuthChanged());
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Oturumunuz sona erdi. Yeniden giriş yapın.')),
        );
      }
    });
  }

  // Kullanıcı giriş/çıkış yaptığında veya hesap değiştirdiğinde çağrılır.
  Future<void> _handleAuthChanged() async {
    if (!mounted) return;

    // Önce eski hesaba ait leaderboard verilerini temizle.
    context.read<LeaderboardProvider>().clearForAccountChange();

    // Hesap değiştiğinde eski bulmaca oturumunu sıfırla
    // ve yeni oturumun günlük bulmacasını yükle.
    _puzzlePageKey.currentState?.resetPuzzleForAccountChange();

    // Yeni oturum durumuna göre SignalR bağlantısını güncelle.
    await _updateSignalRConnection();

    if (!mounted) return;

    // Yeni oturum bilgileriyle leaderboard verisini yeniden yükle.
    _leaderboardPageKey.currentState?.refreshLeaderboard();
    _profilePageKey.currentState?.refreshProfile();
  }

  // SignalR bağlantı güncellemelerini sıraya alır.
  Future<void> _updateSignalRConnection() {
    final previousUpdate =
        _signalRUpdateFuture ?? Future<void>.value();

    final update = previousUpdate.then(
          (_) => _updateSignalRConnectionInternal(),
    );

    _signalRUpdateFuture = update;

    return update.whenComplete(() {
      if (identical(_signalRUpdateFuture, update)) {
        _signalRUpdateFuture = null;
      }
    });
  }

  // Provider üzerinden oturum durumunu kontrol eder
// ve SignalR bağlantısını günceller.
  Future<void> _updateSignalRConnectionInternal() async {
    try {
      final authProvider = context.read<AuthProvider>();

      await authProvider.initialize();

      if (!mounted) return;

      await _signalRService.connect();
    } catch (error) {
      debugPrint(
        'SignalR bağlantı güncelleme hatası: $error',
      );
    }
  }

  // Liderlik tablosunu API üzerinden yeniler.
  void _refreshLeaderboard() {
    debugPrint(
      'NAVIGATION: Liderlik tablosu yenileniyor.',
    );

    if (!mounted) return;

    _leaderboardPageKey.currentState?.refreshLeaderboard();
  }

  void _navigateTo(int index) {
    if (index == _selectedIndex) return;

    setState(() {
      _selectedIndex = index;
    });

    switch (index) {
      case 0:
        _homePageKey.currentState?.refreshStatistics();
        break;

      case 1:
        _puzzlePageKey.currentState?.refreshPuzzle();
        break;

      case 2:
        _leaderboardPageKey.currentState?.refreshLeaderboard();
        break;

      case 3:
        _profilePageKey.currentState?.refreshProfile();
        break;
    }
  }

  @override
  void dispose() {
    _observedAuthProvider?.removeListener(_handleAuthProviderChanged);
    // Sayfa kapatılırken callback'leri temizle.
    _signalRService.onLeaderboardUpdated = null;
    _signalRService.onConnectionRestored = null;

    // SignalR bağlantısını kapat.
    unawaited(_signalRService.disconnect());

    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: IndexedStack(
        index: _selectedIndex,
        children: _pages,
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _selectedIndex,
        onDestinationSelected: _navigateTo,
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home),
            label: 'Ana Sayfa',
          ),
          NavigationDestination(
            icon: Icon(Icons.extension_outlined),
            selectedIcon: Icon(Icons.extension),
            label: 'Bulmaca',
          ),
          NavigationDestination(
            icon: Icon(Icons.leaderboard_outlined),
            selectedIcon: Icon(Icons.leaderboard),
            label: 'Sıralama',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Profil',
          ),
        ],
      ),
    );
  }
}
