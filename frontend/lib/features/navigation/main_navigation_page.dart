
import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/services/leaderboard_signalr_service.dart';
import '../../core/storage/auth_storage.dart';

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
      debugPrint('SIGNALR CALLBACK: onLeaderboardUpdated çalıştı.');
      _refreshLeaderboard();
    };

    _signalRService.onConnectionRestored =
        _refreshLeaderboard;

    // Uygulama açıldığında oturum durumunu kontrol et.
    _updateSignalRConnection();
  }

  // Kullanıcı giriş/çıkış yaptığında çağrılır.
  Future<void> _handleAuthChanged() async {
    await _updateSignalRConnection();
  }

  Future<void> _updateSignalRConnection() async {
    final isLoggedIn =
    await AuthStorage.instance.isLoggedIn();

    if (!mounted) return;

    if (isLoggedIn) {
      await _signalRService.connect();
    } else {
      await _signalRService.disconnect();
    }
  }

  // Liderlik tablosunu API üzerinden yeniler.
  void _refreshLeaderboard() {
    debugPrint('NAVIGATION: Liderlik tablosu yenileniyor.');

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
