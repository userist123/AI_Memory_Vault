package com.nalo.medquery.domain.service;

import org.springframework.stereotype.Service;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.data.domain.PageRequest;
import java.util.List;

import jakarta.transaction.Transactional;

import com.nalo.medquery.domain.repository.IAppointmentRepository;
import com.nalo.medquery.domain.repository.IDoctorRepository;
import com.nalo.medquery.domain.repository.IPatientRepository;
import com.nalo.medquery.domain.validation.IValidateBookAppointment;
import com.nalo.medquery.domain.entity.AppointmentEntity;
import com.nalo.medquery.domain.entity.DoctorEntity;
import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;
import com.nalo.medquery.domain.model.appointment.DetailedAppointmentDataDTO;

@Service
@Transactional
public class AppointmentService {
    @Autowired
    private IAppointmentRepository repository;

    @Autowired
    private IPatientRepository patientRepository;
    
    @Autowired
    private IDoctorRepository doctorRepository;

    @Autowired
    private List<IValidateBookAppointment> validators; //An option to call all 6 validators through an interface 

    public DetailedAppointmentDataDTO schedule(AppointmentRegisterData data) {
        if (!patientRepository.existsById(data.patientId())) {
            throw new IllegalArgumentException("Paciente não existe");
        }
        
        if (data.doctorId() != null && !doctorRepository.existsById(data.doctorId())) {
            throw new IllegalArgumentException("Médico não existe");
        }

        validators.forEach(v -> v.validate(data));

        var patient = patientRepository.getReferenceById(data.patientId());
        var doctor = choseDoctor(data);
        if (doctor == null) {
            throw new IllegalArgumentException("Não existe médico disponível nessa data!");
        }
        
        var appointment = new AppointmentEntity(null, doctor, patient, data.dia());
        repository.save(appointment);

        return new DetailedAppointmentDataDTO(appointment);
    }


    private DoctorEntity choseDoctor(AppointmentRegisterData data){
        if (data.doctorId() != null) {
            return doctorRepository.getReferenceById(data.doctorId());
        }

        if (data.especialidade() == null) {
            throw new IllegalArgumentException("Especialidade é obrigatória quando o médico não é escolhido");
        }
        
        return doctorRepository.findAvailableDoctors(data.especialidade(), data.dia())
                    .stream()
                    .findAny()
                    .orElseThrow(() ->
                        new IllegalArgumentException("Não há médicos disponíveis")
                    );
    }
}
