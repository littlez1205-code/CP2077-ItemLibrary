meta:
  id: cp2077_sav
  title: Cyberpunk 2077 sav.dat container
  endian: le
  ks-version: 0.11
doc: |
  Cyberpunk 2077 v2.31 存档容器。只描述磁盘布局。
  解压后的 payload 不是本文件的顺序字段。
  inventory：i32 自身节点号、u32 子背包数；每个子背包是 u64 背包 ID、u32 物品数。
  玩家背包 ID=1。每个物品先是 16 字节头（含 u64 TweakDBID），再接 itemData 子节点。T002 实现验证。
  itemData 堆叠物品 29 字节：i32 自身节点号、u64 TweakDBID，数量 u32 在区域内 +25。
  TweakDBID = CRC32(ASCII 名字) 放在低 32 位，名字长度放在高 32 位。探针实测与 T002 实现验证。
  依据标注：探针实测，或 T00x 实现验证。
seq:
  - id: magic
    contents: "VASC"
    doc: 探针实测。文件以 VASC 开头。
  - id: save_version
    type: u4
    doc: 探针实测。v2.31 为 269。T001 实现验证白名单。
  - id: game_version
    type: u4
    doc: 探针实测。v2.31 为 2310。2100 显示为 2.1，2110 显示为 2.11。
  - id: metadata
    size: 1
    doc: 探针实测。必须是单字节 0，表示空的元数据字符串。
  - id: timestamp
    type: u8
    doc: 探针实测。紧跟在元数据字节之后，所以不是 8 字节对齐。
  - id: archive_version
    type: u4
    doc: 探针实测。位于偏移 21。
  - id: fzlc
    contents: "FZLC"
    doc: 探针实测。分块表魔数，偏移 25。
  - id: chunk_count
    type: u4
    doc: 探针实测。合法范围 1..512。T001 实现验证。
  - id: chunk_slots
    type: chunk_slot
    repeat: expr
    repeat-expr: 512
    doc: |
      探针实测。槽位表固定 512 项，不随 chunk_count 缩短。
      未用槽位可能残留上一次写入的旧数据，不能当成全 0。
      AutoSave-13 的 80 号槽位留有超出节点表的偏移，游戏会忽略，读写必须原样保留。T007 实现验证。
  - id: chunks
    type: xlz4_chunk
    repeat: expr
    repeat-expr: chunk_count
    doc: 探针实测。只使用前 chunk_count 个槽位。首块文件偏移为 0x1821。
  - id: node_table
    size: _io.size - _io.pos - 8
    doc: |
      探针实测。NODE 魔数（磁盘序 45 44 4F 4E）开头。
      随后是紧凑整数格式的节点个数，再是每个节点：
      紧凑整数名字长度（当前存档为负数，绝对值是 ASCII 字节数）、名字、
      i32 next、i32 child、i32 文件偏移、i32 长度。
      紧凑整数：首字节 bit7 符号、bit6 续位、低 6 位是值；后续字节 bit7 续位、低 7 位是值。
      T002 实现验证。节点偏移按「首块文件偏移 + 解压流偏移」计。
  - id: node_table_offset
    type: u4
    doc: 探针实测。指向 node_table 的文件偏移。位于 DONE 之前。
  - id: done
    contents: "DONE"
    doc: 探针实测。文件最后 4 字节。注意与 NODE 的字节序不同。
types:
  chunk_slot:
    seq:
      - id: file_offset
        type: u4
        doc: 探针实测。该块在文件中的起点，含 8 字节 XLZ4 头。
      - id: compressed_length
        type: u4
        doc: 探针实测。含 8 字节块头。T003 实现验证。
      - id: decompressed_length
        type: u4
        doc: 探针实测。满块为 0x40000，最后一块可以更短。
  xlz4_chunk:
    seq:
      - id: magic
        type: u4
        doc: 探针实测。小端 0x584C5A34，磁盘序 34 5A 4C 58。
      - id: decompressed_length
        type: u4
        doc: 探针实测。与槽位里的解压长度一致。
      - id: compressed
        size: chunk_slots[_index].compressed_length - 8
        doc: 探针实测。原始 LZ4 block，不含框架。T001 实现验证。
