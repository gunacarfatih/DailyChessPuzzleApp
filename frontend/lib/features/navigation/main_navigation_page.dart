import 'dart:async';

import 'package:flutter/foundation.dart';
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

class _MainNavigationPageState
    extends State<MainNavigationPage>
    with WidgetsBindingObserver {
  int _selectedIndex = 0;

  final GlobalKey<HomePageState> _homePageKey =
  GlobalKey<HomePageState>();

  final GlobalKey<PuzzlePageState> _puzzlePageKey =
  GlobalKey<PuzzlePageState>();

  final GlobalKey<LeaderboardPageState>
  _leaderboardPageKey =
  GlobalKey<LeaderboardPageState>();

  final GlobalKey<ProfilePageState> _profilePageKey =
  GlobalKey<ProfilePageState>();

  final LeaderboardSignalRService _signalRService =
  LeaderboardSignalRService();

  Future<void>? _signalRUpdateFuture;

  AuthProvider? _observedAuthProvider;

  bool _lastAuthState = false;

  String? _lastCountryCode;

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

    WidgetsBinding.instance.addObserver(this);

    // SignalR bildirimleri doğrudan Provider'a iletilir.
    _signalRService.onLeaderboardEvent = (event) {
      if (!mounted) return;

      context
          .read<LeaderboardProvider>()
          .handleLeaderboardEvent(event);
    };

    // Yeniden bağlantıda kaçırılmış güncellemeler alınır.
    _signalRService.onConnectionRestored = () {
      if (!mounted) return;

      unawaited(_refreshAllLeaderboards());
    };

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      unawaited(_initializeSignalR());
    });
  }

  Future<void> _initializeSignalR() async {
    await _updateSignalRConnection();

    if (!mounted) return;

    await _refreshAllLeaderboards();
  }

  @override
  void didChangeAppLifecycleState(
      AppLifecycleState state,
      ) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_handleAppResumed());
    }
  }

  Future<void> _handleAppResumed() async {
    if (!mounted) return;

    try {
      await context
          .read<AuthProvider>()
          .syncCountryFromDevice();

      if (!mounted) return;

      await _updateSignalRConnection();

      if (!mounted) return;

      await _refreshAllLeaderboards();
    } catch (error, stackTrace) {
      if (kDebugMode) {
        debugPrint(
          'Uygulama yeniden açılma hatası: $error',
        );

        debugPrintStack(stackTrace: stackTrace);
      }
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();

    final provider = context.read<AuthProvider>();

    if (identical(
      provider,
      _observedAuthProvider,
    )) {
      return;
    }

    _observedAuthProvider?.removeListener(
      _handleAuthProviderChanged,
    );

    _observedAuthProvider = provider;

    _lastAuthState = provider.isLoggedIn;

    _lastCountryCode = _normalizeCountry(
      provider.countryCode,
    );

    provider.addListener(
      _handleAuthProviderChanged,
    );
  }

  String? _normalizeCountry(String? country) {
    final normalized =
    country?.trim().toUpperCase();

    if (normalized == null ||
        normalized.isEmpty) {
      return null;
    }

    return normalized;
  }

  void _handleAuthProviderChanged() {
    final provider = _observedAuthProvider;

    if (provider == null || !mounted) {
      return;
    }

    final isLoggedIn = provider.isLoggedIn;

    final countryCode = _normalizeCountry(
      provider.countryCode,
    );

    final sessionExpired =
        _lastAuthState && !isLoggedIn;

    final countryChanged =
        _lastCountryCode != countryCode;

    _lastAuthState = isLoggedIn;
    _lastCountryCode = countryCode;

    if (sessionExpired) {
      setState(() {
        _selectedIndex = 3;
      });

      unawaited(_handleAuthChanged());

      WidgetsBinding.instance.addPostFrameCallback(
            (_) {
          if (!mounted) return;

          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text(
                'Oturumunuz sona erdi. '
                    'Yeniden giriş yapın.',
              ),
            ),
          );
        },
      );

      return;
    }

    if (countryChanged && isLoggedIn) {
      unawaited(_handleCountryChanged());
    }
  }

  Future<void> _handleCountryChanged() async {
    if (!mounted) return;

    await _updateSignalRConnection();

    if (!mounted) return;

    await _refreshAllLeaderboards();
  }

  // Giriş, çıkış veya hesap değişikliği.
  Future<void> _handleAuthChanged() async {
    if (!mounted) return;

    final leaderboardProvider =
    context.read<LeaderboardProvider>();

    leaderboardProvider.clearForAccountChange();

    _puzzlePageKey.currentState
        ?.resetPuzzleForAccountChange();

    await _updateSignalRConnection();

    if (!mounted) return;

    await _refreshAllLeaderboards();

    if (!mounted) return;

    _leaderboardPageKey.currentState
        ?.refreshLeaderboard();

    _profilePageKey.currentState
        ?.refreshProfile();
  }

  // SignalR bağlantı güncellemelerini sıraya alır.
  Future<void> _updateSignalRConnection() {
    final previousUpdate =
        _signalRUpdateFuture ??
            Future<void>.value();

    final update = previousUpdate.then(
          (_) => _updateSignalRConnectionInternal(),
    );

    _signalRUpdateFuture = update;

    return update.whenComplete(() {
      if (identical(
        _signalRUpdateFuture,
        update,
      )) {
        _signalRUpdateFuture = null;
      }
    });
  }

  Future<void>
  _updateSignalRConnectionInternal() async {
    if (!mounted) return;

    try {
      final authProvider =
      context.read<AuthProvider>();

      await authProvider.initialize();

      if (!mounted) return;

      await _signalRService.connect();

      if (!mounted) return;

      await _signalRService.refreshSubscriptions();
    } catch (error, stackTrace) {
      if (kDebugMode) {
        debugPrint(
          'SignalR bağlantı güncelleme hatası: '
              '$error',
        );

        debugPrintStack(stackTrace: stackTrace);
      }
    }
  }

  // Global ve bilinen ülke tablolarını yeniler.
  Future<void> _refreshAllLeaderboards() async {
    if (!mounted) return;

    final authProvider =
    context.read<AuthProvider>();

    final leaderboardProvider =
    context.read<LeaderboardProvider>();

    await leaderboardProvider
        .refreshAllKnownLeaderboards(
      country: _normalizeCountry(
        authProvider.countryCode,
      ),
    );
  }

  void _navigateTo(int index) {
    if (index == _selectedIndex) {
      return;
    }

    setState(() {
      _selectedIndex = index;
    });

    switch (index) {
      case 0:
        _homePageKey.currentState
            ?.refreshStatistics();
        break;

      case 1:
        _puzzlePageKey.currentState
            ?.refreshPuzzle();
        break;

      case 2:
        _leaderboardPageKey.currentState
            ?.refreshLeaderboard();
        break;

      case 3:
        _profilePageKey.currentState
            ?.refreshProfile();
        break;
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);

    _observedAuthProvider?.removeListener(
      _handleAuthProviderChanged,
    );

    _signalRService.onLeaderboardUpdated = null;

    _signalRService.onLeaderboardEvent = null;

    _signalRService.onConnectionRestored = null;

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