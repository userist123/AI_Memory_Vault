package com.example.sismedico.repository;

import com.example.sismedico.entity.Horario;
import com.example.sismedico.entity.Medico;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;

public interface HorarioRepository extends JpaRepository<Horario, Long> {

    List<Horario> findByMedico(Medico medico);

}