import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../providers/profile_provider.dart';
import 'profile_watch_page.dart';

class ProfileHistoryPage extends StatefulWidget {
  const ProfileHistoryPage({super.key});
  @override
  State<ProfileHistoryPage> createState() => _ProfileHistoryPageState();
}

class _ProfileHistoryPageState extends State<ProfileHistoryPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback(
      (_) => context.read<ProfileProvider>().loadHistory(),
    );
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ProfileProvider>();
    return Scaffold(
      appBar: AppBar(title: const Text('Çözüm Geçmişi')),
      body: provider.loading && provider.history.isEmpty
          ? const Center(child: CircularProgressIndicator())
          : provider.error != null && provider.history.isEmpty
          ? Center(child: Text(provider.error!))
          : ListView.builder(
              padding: const EdgeInsets.all(12),
              itemCount: provider.history.length,
              itemBuilder: (context, index) {
                final item = provider.history[index];
                final date = DateTime.tryParse(
                  item['submittedAtUtc']?.toString() ?? '',
                )?.toLocal();
                return Card(
                  color: const Color(0xFF111111),
                  child: ListTile(
                    leading: const Icon(
                      Icons.sports_esports,
                      color: Color(0xFFD4AF37),
                    ),
                    title: Text(
                      '${item['kind'] == 'bonus' ? 'Bonus' : 'Günlük'} • ${item['rating'] ?? ''} rating',
                      style: const TextStyle(color: Colors.white),
                    ),
                    subtitle: Text(
                      '${date == null ? '' : '${date.day}.${date.month}.${date.year}'}  •  ${item['seconds']} sn  •  ${item['xp']} XP\n${item['wrongMoves']} hata · ipucu ${item['usedHint'] == true ? 'var' : 'yok'}',
                      style: const TextStyle(color: Colors.white60),
                    ),
                    isThreeLine: true,
                    trailing: const Icon(
                      Icons.play_circle_outline,
                      color: Color(0xFFD4AF37),
                    ),
                    onTap: () => Navigator.of(context).push(
                      MaterialPageRoute(
                        builder: (_) => ProfileWatchPage(
                          attemptId: (item['id'] as num).toInt(),
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
    );
  }
}
